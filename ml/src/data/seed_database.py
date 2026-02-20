"""Database seeding for the no-show predictor ML system.

Loads parquet files containing synthetic data into Azure SQL Database.
Uses credential-less authentication via DefaultAzureCredential.
"""

import argparse
import logging
import os
import struct
import sys
from pathlib import Path

import numpy as np
import pandas as pd
import pyodbc
from azure.identity import DefaultAzureCredential

# Force unbuffered output for real-time progress
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(line_buffering=True)

logging.basicConfig(
    level=logging.INFO,
    format="%(message)s",
    handlers=[logging.StreamHandler(sys.stdout)],
)
logger = logging.getLogger(__name__)


# =============================================================================
# Database Connection
# =============================================================================


def get_odbc_driver() -> str:
    """Get the best available ODBC driver for SQL Server.

    Returns:
        ODBC driver name string
    """
    all_drivers = pyodbc.drivers()
    for preferred in ["ODBC Driver 18 for SQL Server", "ODBC Driver 17 for SQL Server"]:
        if preferred in all_drivers:
            return preferred
    # Fallback to any SQL Server driver
    sql_drivers = [d for d in all_drivers if "SQL Server" in d]
    if sql_drivers:
        return sql_drivers[-1]
    return "ODBC Driver 17 for SQL Server"


def get_connection_string(server: str, database: str) -> str:
    """Get connection string for Azure SQL Database.

    Uses Entra ID (AAD) authentication via access token.

    Args:
        server: SQL Server hostname (e.g., sql-noshowdemo-dev.database.windows.net)
        database: Database name

    Returns:
        ODBC connection string
    """
    driver = get_odbc_driver()
    return (
        f"Driver={{{driver}}};"
        f"Server={server};"
        f"Database={database};"
        f"Encrypt=yes;"
        f"TrustServerCertificate=no;"
        f"Connection Timeout=60;"
    )


def get_access_token_struct() -> bytes:
    """Get access token for Azure SQL Database as pyodbc-compatible struct.

    Returns:
        Token bytes in the format expected by pyodbc attrs_before
    """
    credential = DefaultAzureCredential()
    token = credential.get_token("https://database.windows.net/.default")
    token_bytes = token.token.encode("utf-16-le")
    # Pack as 4-byte little-endian length prefix + token bytes
    return struct.pack("<I", len(token_bytes)) + token_bytes


def create_connection(server: str, database: str) -> pyodbc.Connection:
    """Create a connection to Azure SQL Database with AAD authentication.

    Args:
        server: SQL Server hostname
        database: Database name

    Returns:
        pyodbc Connection object
    """
    connection_string = get_connection_string(server, database)
    token_struct = get_access_token_struct()

    # SQL_COPT_SS_ACCESS_TOKEN = 1256
    connection = pyodbc.connect(connection_string, attrs_before={1256: token_struct})
    connection.autocommit = False
    return connection


# =============================================================================
# Data Loading
# =============================================================================


def load_parquet_files(data_dir: Path) -> dict[str, pd.DataFrame]:
    """Load all parquet files from the data directory.

    Args:
        data_dir: Path to directory containing parquet files

    Returns:
        Dictionary mapping table names to DataFrames
    """
    data_dir = Path(data_dir)
    files = {
        "patients": "patients.parquet",
        "providers": "providers.parquet",
        "departments": "departments.parquet",
        "insurance": "insurance.parquet",
        "appointments": "appointments.parquet",
    }

    dataframes = {}
    for table_name, filename in files.items():
        filepath = data_dir / filename
        if filepath.exists():
            logger.info(f"Loading {filepath}...")
            dataframes[table_name] = pd.read_parquet(filepath)
            logger.info(f"  Loaded {len(dataframes[table_name]):,} records")
        else:
            raise FileNotFoundError(f"Required file not found: {filepath}")

    return dataframes


# =============================================================================
# Column Mapping
# =============================================================================

# Columns to insert for each table (must exist in both parquet and SQL schema)
TABLE_COLUMNS = {
    "Departments": [
        "departmentid",
        "departmentname",
        "departmentspecialty",
        "billingname",
        "placeofservicecode",
        "placeofservicetype",
        "providergroupid",
        "departmentgroup",
        "contextid",
        "contextname",
        "market",
        "division",
        "business_unit",
    ],
    "Providers": [
        "providerid",
        "pro_providerid",
        "providerfirstname",
        "providerlastname",
        "providertype",
        "providertypecategory",
        "provider_specialty",
        "provider_specialty_service_line",
        "providernpinumber",
        "provider_affiliation",
        "entitytype",
        "billableyn",
        "patientfacingname",
    ],
    "Patients": [
        "patientid",
        "enterpriseid",
        "patient_gender",
        "patient_age_bucket",
        "patient_race_ethnicity",
        "patient_email",
        "patient_zip_code",
        "portal_enterpriseid",
        "portal_last_login",
        "historical_no_show_count",
        "historical_no_show_rate",
    ],
    "Insurance": [
        "primarypatientinsuranceid",
        "patientid",
        "sipg1",
        "sipg2",
        "insurance_plan_1_company_description",
        "insurance_group_id",
    ],
    "Appointments": [
        "appointmentid",
        "parentappointmentid",
        "patientid",
        "providerid",
        "departmentid",
        "referringproviderid",
        "appointmentdate",
        "appointmentstarttime",
        "appointmentdatetime",
        "appointmentduration",
        "appointmenttypeid",
        "appointmenttypename",
        "appointmentstatus",
        "appointmentcreateddatetime",
        "appointmentscheduleddatetime",
        "appointmentcheckindatetime",
        "appointmentcheckoutdatetime",
        "appointmentcancelleddatetime",
        "webschedulableyn",
        "virtual_flag",
        "new_patient_flag",
    ],
}

# Parquet file to SQL table name mapping
PARQUET_TO_TABLE = {
    "departments": "Departments",
    "providers": "Providers",
    "patients": "Patients",
    "insurance": "Insurance",
    "appointments": "Appointments",
}


# =============================================================================
# Database Operations
# =============================================================================


def truncate_tables(connection: pyodbc.Connection) -> None:
    """Truncate all tables before loading fresh data.

    Uses DELETE with foreign key handling instead of TRUNCATE
    to work around FK constraints.
    """
    cursor = connection.cursor()

    # Order matters due to foreign key constraints
    tables_to_clear = [
        "Predictions",
        "Appointments",
        "Insurance",
        "Patients",
        "Providers",
        "Departments",
    ]

    logger.info("Clearing existing data...")
    for table in tables_to_clear:
        try:
            cursor.execute(f"DELETE FROM dbo.{table}")
            connection.commit()  # Commit each delete immediately
            logger.info(f"  Cleared {table}")
        except pyodbc.Error as e:
            logger.warning(f"  Could not clear {table}: {e}")
            connection.rollback()

    cursor.close()


def prepare_dataframe(df: pd.DataFrame, table_name: str) -> pd.DataFrame:
    """Prepare DataFrame for insertion by handling computed columns and type conversions.

    Args:
        df: Input DataFrame
        table_name: Target SQL table name

    Returns:
        Prepared DataFrame with only columns that should be inserted
    """
    df = df.copy()

    # Handle appointments specially - create appointmentdatetime from date + time
    if table_name == "Appointments":
        df["appointmentstarttime"] = df["appointmentstarttime"].astype(str)
        df["appointmentdate_str"] = df["appointmentdate"].astype(str)
        df["appointmentdatetime"] = pd.to_datetime(
            df["appointmentdate_str"] + " " + df["appointmentstarttime"],
            format="%Y-%m-%d %H:%M",
            errors="coerce",
        )
        # Convert appointmentdate to string format SQL expects
        df["appointmentdate"] = df["appointmentdate"].astype(str)

    # Filter to only columns that exist in both parquet and table definition
    expected_cols = TABLE_COLUMNS.get(table_name, [])
    available_cols = [c for c in expected_cols if c in df.columns]
    df = df[available_cols]

    # Replace NaN with None for SQL
    df = df.replace({np.nan: None, pd.NaT: None})

    # Convert numpy int64 to Python int for pyodbc compatibility
    for col in df.columns:
        if df[col].dtype == "int64":
            df[col] = df[col].astype(object).where(df[col].notna(), None)

    return df


def insert_dataframe(
    connection: pyodbc.Connection,
    df: pd.DataFrame,
    table_name: str,
    batch_size: int = 100,
    server: str = "",
    database: str = "",
) -> tuple[int, pyodbc.Connection]:
    """Insert DataFrame records into a SQL table using fast_executemany.

    Args:
        connection: Database connection
        df: DataFrame to insert
        table_name: Target table name
        batch_size: Number of records per batch
        server: SQL Server hostname (for reconnection)
        database: Database name (for reconnection)

    Returns:
        Tuple of (number of records inserted, connection) — connection may be
        refreshed if the original was dropped by the server.
    """
    cursor = connection.cursor()
    cursor.fast_executemany = True  # Critical for performance!

    # Prepare DataFrame
    df = prepare_dataframe(df, table_name)

    # Build parameterized INSERT statement
    cols = list(df.columns)
    placeholders = ", ".join(["?" for _ in cols])
    column_list = ", ".join(cols)
    insert_sql = f"INSERT INTO dbo.{table_name} ({column_list}) VALUES ({placeholders})"

    # Convert DataFrame to list of tuples, handling NaN properly
    rows = [tuple(None if pd.isna(v) else v for v in row) for row in df.values]

    total = len(rows)
    inserted = 0
    max_retries = 3

    for i in range(0, total, batch_size):
        batch = rows[i : i + batch_size]
        for attempt in range(max_retries):
            try:
                cursor.executemany(insert_sql, batch)
                connection.commit()
                inserted += len(batch)
                if inserted % 1000 == 0 or inserted == total:
                    logger.info(f"    Progress: {inserted:,}/{total:,} ({100 * inserted // total}%)")
                break
            except pyodbc.Error as e:
                error_code = getattr(e, 'args', [('',)])[0] if e.args else ''
                is_connection_error = any(
                    code in str(error_code) for code in ['08S01', '08001', '01000']
                )
                if is_connection_error and attempt < max_retries - 1 and server:
                    logger.warning(f"    Connection lost at offset {i}, reconnecting (attempt {attempt + 2}/{max_retries})...")
                    import time
                    time.sleep(5 * (attempt + 1))
                    try:
                        connection = create_connection(server, database)
                        cursor = connection.cursor()
                        cursor.fast_executemany = True
                        continue
                    except Exception as reconnect_err:
                        logger.error(f"    Reconnection failed: {reconnect_err}")
                logger.error(f"Error inserting batch at offset {i}: {e}")
                logger.error(f"Columns: {cols}")
                try:
                    connection.rollback()
                except Exception:
                    pass
                raise

    cursor.close()
    return inserted, connection


def seed_database(
    connection: pyodbc.Connection,
    dataframes: dict[str, pd.DataFrame],
    truncate: bool = True,
    server: str = "",
    database: str = "",
) -> tuple[dict[str, int], pyodbc.Connection]:
    """Seed the database with all entity data.

    Args:
        connection: Database connection
        dataframes: Dictionary of entity DataFrames (keyed by parquet name)
        truncate: Whether to truncate tables before loading
        server: SQL Server hostname (for reconnection)
        database: Database name (for reconnection)

    Returns:
        Tuple of (dict mapping table names to record counts, connection)
    """
    if truncate:
        truncate_tables(connection)

    # Insert order matters due to foreign key constraints
    insert_order = ["departments", "providers", "patients", "insurance", "appointments"]
    results = {}

    for parquet_name in insert_order:
        if parquet_name in dataframes:
            table_name = PARQUET_TO_TABLE[parquet_name]
            logger.info(f"\nLoading {parquet_name}...")
            logger.info(f"  Read {len(dataframes[parquet_name]):,} records from parquet")
            logger.info(f"  Starting insert into {table_name}...")

            count, connection = insert_dataframe(
                connection=connection,
                df=dataframes[parquet_name],
                table_name=table_name,
                server=server,
                database=database,
            )
            results[table_name] = count
            logger.info(f"  Inserted {count:,} records into {table_name}")

    return results, connection


# =============================================================================
# Validation
# =============================================================================


def validate_data(connection: pyodbc.Connection) -> dict[str, int]:
    """Validate that data was inserted correctly.

    Args:
        connection: Database connection

    Returns:
        Dictionary mapping table names to record counts
    """
    cursor = connection.cursor()
    tables = ["Departments", "Providers", "Patients", "Insurance", "Appointments"]
    counts = {}

    logger.info("\nValidating data...")
    for table in tables:
        cursor.execute(f"SELECT COUNT(*) FROM dbo.{table}")
        count = cursor.fetchone()[0]
        counts[table] = count
        logger.info(f"  {table}: {count:,} records")

    # Validate no-show rate
    cursor.execute("""
        SELECT
            COUNT(*) as total,
            SUM(CASE WHEN appointmentstatus = 'No Show' THEN 1 ELSE 0 END) as no_shows
        FROM dbo.Appointments
        WHERE appointmentdate < GETDATE()
    """)
    row = cursor.fetchone()
    total, no_shows = row
    if total > 0:
        no_show_rate = no_shows / total
        logger.info(f"\nNo-show rate for past appointments: {no_show_rate:.1%} ({no_shows:,}/{total:,})")

    cursor.close()
    return counts


# =============================================================================
# Main Entry Point
# =============================================================================


def seed_from_parquet(
    server: str,
    database: str,
    data_dir: Path | str,
    truncate: bool = True,
    validate: bool = True,
) -> dict[str, int]:
    """Main function to seed database from parquet files.

    Args:
        server: SQL Server hostname
        database: Database name
        data_dir: Directory containing parquet files
        truncate: Whether to truncate tables before loading
        validate: Whether to validate data after loading

    Returns:
        Dictionary mapping table names to record counts
    """
    data_dir = Path(data_dir)

    logger.info(f"Connecting to {server}/{database}...")
    logger.info(f"Using driver: {get_odbc_driver()}")

    # Load data
    dataframes = load_parquet_files(data_dir)

    # Connect and seed
    connection = create_connection(server, database)
    logger.info("Connected successfully!")

    try:
        results, connection = seed_database(
            connection, dataframes, truncate=truncate,
            server=server, database=database,
        )

        if validate:
            validate_data(connection)

        logger.info("\nData seeding complete!")
        return results

    finally:
        connection.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Seed Azure SQL Database with synthetic data")
    parser.add_argument(
        "--server",
        type=str,
        default=os.environ.get("SQL_SERVER", ""),
        help="SQL Server hostname (e.g., sql-noshowdemo-dev.database.windows.net)",
    )
    parser.add_argument(
        "--database",
        type=str,
        default=os.environ.get("SQL_DATABASE", "sqldb-noshow"),
        help="Database name",
    )
    parser.add_argument(
        "--data-dir",
        type=str,
        default="./data/synthetic",
        help="Directory containing parquet files",
    )
    parser.add_argument(
        "--no-truncate",
        action="store_true",
        help="Don't truncate tables before loading (append mode)",
    )
    parser.add_argument(
        "--no-validate",
        action="store_true",
        help="Skip validation after loading",
    )

    args = parser.parse_args()

    if not args.server:
        raise ValueError(
            "SQL Server hostname required. Set SQL_SERVER environment variable or use --server argument."
        )

    seed_from_parquet(
        server=args.server,
        database=args.database,
        data_dir=args.data_dir,
        truncate=not args.no_truncate,
        validate=not args.no_validate,
    )
