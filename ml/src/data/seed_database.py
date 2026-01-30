"""Database seeding for the no-show predictor ML system.

Loads parquet files containing synthetic data into Azure SQL Database.
Uses credential-less authentication via DefaultAzureCredential.
"""

import argparse
import logging
import os
from pathlib import Path

import pandas as pd
import pyodbc
from azure.identity import DefaultAzureCredential

logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(levelname)s - %(message)s")
logger = logging.getLogger(__name__)


# =============================================================================
# Database Connection
# =============================================================================


def get_connection_string(server: str, database: str) -> str:
    """Get connection string for Azure SQL Database.

    Uses Entra ID (AAD) authentication via access token.

    Args:
        server: SQL Server hostname (e.g., sql-noshowdemo-dev.database.windows.net)
        database: Database name

    Returns:
        ODBC connection string
    """
    return (
        f"Driver={{ODBC Driver 18 for SQL Server}};"
        f"Server={server};"
        f"Database={database};"
        f"Encrypt=yes;"
        f"TrustServerCertificate=no;"
    )


def get_access_token() -> str:
    """Get access token for Azure SQL Database using DefaultAzureCredential."""
    credential = DefaultAzureCredential()
    token = credential.get_token("https://database.windows.net/.default")
    return token.token


def create_connection(server: str, database: str) -> pyodbc.Connection:
    """Create a connection to Azure SQL Database with AAD authentication.

    Args:
        server: SQL Server hostname
        database: Database name

    Returns:
        pyodbc Connection object
    """
    connection_string = get_connection_string(server, database)
    access_token = get_access_token()

    # Encode the access token for pyodbc
    token_bytes = access_token.encode("utf-16-le")
    token_struct = bytes([len(token_bytes) & 0xFF, (len(token_bytes) >> 8) & 0xFF]) + token_bytes

    attrs = {1256: token_struct}  # SQL_COPT_SS_ACCESS_TOKEN

    connection = pyodbc.connect(connection_string, attrs_before=attrs)
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

# Map DataFrame columns to SQL table columns
COLUMN_MAPPINGS = {
    "patients": {
        "patientid": "patientid",
        "enterpriseid": "enterpriseid",
        "patient_gender": "patient_gender",
        "patient_age_bucket": "patient_age_bucket",
        "patient_race_ethnicity": "patient_race_ethnicity",
        "patient_email": "patient_email",
        "patient_zip_code": "patient_zip_code",
        "portal_enterpriseid": "portal_enterpriseid",
        "portal_last_login": "portal_last_login",
        "historical_no_show_count": "historical_no_show_count",
        "historical_no_show_rate": "historical_no_show_rate",
    },
    "providers": {
        "providerid": "providerid",
        "pro_providerid": "pro_providerid",
        "providerfirstname": "providerfirstname",
        "providerlastname": "providerlastname",
        "providertype": "providertype",
        "providertypecategory": "providertypecategory",
        "provider_specialty": "provider_specialty",
        "provider_specialty_service_line": "provider_specialty_service_line",
        "providernpinumber": "providernpinumber",
        "provider_affiliation": "provider_affiliation",
        "entitytype": "entitytype",
        "billableyn": "billableyn",
        "patientfacingname": "patientfacingname",
    },
    "departments": {
        "departmentid": "departmentid",
        "departmentname": "departmentname",
        "departmentspecialty": "departmentspecialty",
        "billingname": "billingname",
        "placeofservicecode": "placeofservicecode",
        "placeofservicetype": "placeofservicetype",
        "providergroupid": "providergroupid",
        "departmentgroup": "departmentgroup",
        "contextid": "contextid",
        "contextname": "contextname",
        "market": "market",
        "division": "division",
        "business_unit": "business_unit",
    },
    "insurance": {
        "primarypatientinsuranceid": "primarypatientinsuranceid",
        "patientid": "patientid",
        "sipg1": "sipg1",
        "sipg2": "sipg2",
        "insurance_plan_1_company_description": "insurance_plan_1_company_description",
        "insurance_group_id": "insurance_group_id",
    },
    "appointments": {
        "appointmentid": "appointmentid",
        "parentappointmentid": "parentappointmentid",
        "patientid": "patientid",
        "providerid": "providerid",
        "departmentid": "departmentid",
        "referringproviderid": "referringproviderid",
        "appointmentdate": "appointmentdate",
        "appointmentstarttime": "appointmentstarttime",
        "appointmentduration": "appointmentduration",
        "appointmenttypeid": "appointmenttypeid",
        "appointmenttypename": "appointmenttypename",
        "appointmentstatus": "appointmentstatus",
        "appointmentcreateddatetime": "appointmentcreateddatetime",
        "appointmentscheduleddatetime": "appointmentscheduleddatetime",
        "appointmentcheckindatetime": "appointmentcheckindatetime",
        "appointmentcheckoutdatetime": "appointmentcheckoutdatetime",
        "appointmentcancelleddatetime": "appointmentcancelleddatetime",
        "webschedulableyn": "webschedulableyn",
        "virtual_flag": "virtual_flag",
        "new_patient_flag": "new_patient_flag",
    },
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
        "predictions",
        "appointments",
        "insurance",
        "patients",
        "providers",
        "departments",
    ]

    logger.info("Clearing existing data...")
    for table in tables_to_clear:
        try:
            cursor.execute(f"DELETE FROM {table}")
            logger.info(f"  Cleared {table}")
        except pyodbc.Error as e:
            logger.warning(f"  Could not clear {table}: {e}")

    connection.commit()
    cursor.close()


def insert_dataframe(
    connection: pyodbc.Connection,
    df: pd.DataFrame,
    table_name: str,
    column_mapping: dict[str, str],
    batch_size: int = 1000,
) -> int:
    """Insert DataFrame records into a SQL table using batch inserts.

    Args:
        connection: Database connection
        df: DataFrame to insert
        table_name: Target table name
        column_mapping: DataFrame column to SQL column mapping
        batch_size: Number of records per batch

    Returns:
        Number of records inserted
    """
    cursor = connection.cursor()

    # Filter DataFrame to only include mapped columns that exist
    available_columns = [col for col in column_mapping.keys() if col in df.columns]
    sql_columns = [column_mapping[col] for col in available_columns]

    df_subset = df[available_columns].copy()

    # Handle NaN/None values
    df_subset = df_subset.where(pd.notnull(df_subset), None)

    # Build parameterized INSERT statement
    placeholders = ", ".join(["?" for _ in sql_columns])
    column_list = ", ".join(sql_columns)
    insert_sql = f"INSERT INTO {table_name} ({column_list}) VALUES ({placeholders})"

    # Insert in batches
    total_inserted = 0
    records = df_subset.values.tolist()

    for i in range(0, len(records), batch_size):
        batch = records[i : i + batch_size]
        try:
            cursor.executemany(insert_sql, batch)
            connection.commit()
            total_inserted += len(batch)
            if total_inserted % 10000 == 0:
                logger.info(f"  Inserted {total_inserted:,} records...")
        except pyodbc.Error as e:
            logger.error(f"Error inserting batch at offset {i}: {e}")
            connection.rollback()
            raise

    cursor.close()
    return total_inserted


def seed_database(
    connection: pyodbc.Connection,
    dataframes: dict[str, pd.DataFrame],
    truncate: bool = True,
) -> dict[str, int]:
    """Seed the database with all entity data.

    Args:
        connection: Database connection
        dataframes: Dictionary of entity DataFrames
        truncate: Whether to truncate tables before loading

    Returns:
        Dictionary mapping table names to record counts inserted
    """
    if truncate:
        truncate_tables(connection)

    # Insert order matters due to foreign key constraints
    insert_order = ["departments", "providers", "patients", "insurance", "appointments"]
    results = {}

    for table_name in insert_order:
        if table_name in dataframes:
            logger.info(f"Inserting {table_name}...")
            count = insert_dataframe(
                connection=connection,
                df=dataframes[table_name],
                table_name=table_name,
                column_mapping=COLUMN_MAPPINGS[table_name],
            )
            results[table_name] = count
            logger.info(f"  Inserted {count:,} records into {table_name}")

    return results


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
    tables = ["departments", "providers", "patients", "insurance", "appointments"]
    counts = {}

    logger.info("Validating data...")
    for table in tables:
        cursor.execute(f"SELECT COUNT(*) FROM {table}")
        count = cursor.fetchone()[0]
        counts[table] = count
        logger.info(f"  {table}: {count:,} records")

    # Validate no-show rate
    cursor.execute("""
        SELECT
            COUNT(*) as total,
            SUM(CASE WHEN appointmentstatus = 'No Show' THEN 1 ELSE 0 END) as no_shows
        FROM appointments
        WHERE appointmentdate < GETDATE()
    """)
    row = cursor.fetchone()
    total, no_shows = row
    no_show_rate = no_shows / max(1, total)
    logger.info(f"\nNo-show rate for past appointments: {no_show_rate:.1%}")

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

    logger.info("=== Database Seeding ===")
    logger.info(f"Server: {server}")
    logger.info(f"Database: {database}")
    logger.info(f"Data directory: {data_dir}")

    # Load data
    dataframes = load_parquet_files(data_dir)

    # Connect and seed
    logger.info("\nConnecting to database...")
    connection = create_connection(server, database)

    try:
        results = seed_database(connection, dataframes, truncate=truncate)

        if validate:
            validate_data(connection)

        logger.info("\n=== Seeding Complete ===")
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
        default=os.environ.get("SQL_DATABASE", "sqldb-noshowdemo-dev"),
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
