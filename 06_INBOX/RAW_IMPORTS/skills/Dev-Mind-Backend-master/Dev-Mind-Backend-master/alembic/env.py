import os
from dotenv import load_dotenv
load_dotenv()

from logging.config import fileConfig

from sqlalchemy import pool
import asyncio
from sqlalchemy.ext.asyncio import async_engine_from_config


from alembic import context

from app.database import Base

# this is the Alembic Config object, which provides
# access to the values within the .ini file in use.
config = context.config

# Fetch the database URL from your environment variables
database_url = os.getenv("DATABASE_URL")

# Inject the database URL into the Alembic ini settings
if database_url:
    # If running locally, swap '@db:' with '@127.0.0.1:'
    if "@db:" in database_url:
        database_url = database_url.replace("@db:5432", "@localhost:5433")
    
    # Inject the modified URL back into Alembic
    config.set_main_option("sqlalchemy.url", database_url)

# Interpret the config file for Python logging.
# This line sets up loggers basically.
if config.config_file_name is not None:
    fileConfig(config.config_file_name)

# add your model's MetaData object here
# for 'autogenerate' support
# from myapp import mymodel
# target_metadata = mymodel.Base.metadata
target_metadata = Base.metadata

# other values from the config, defined by the needs of env.py,
# can be acquired:
# my_important_option = config.get_main_option("my_important_option")
# ... etc.


def run_migrations_offline() -> None:
    """Run migrations in 'offline' mode.

    This configures the context with just a URL
    and not an Engine, though an Engine is acceptable
    here as well.  By skipping the Engine creation
    we don't even need a DBAPI to be available.

    Calls to context.execute() here emit the given string to the
    script output.

    """
    url = config.get_main_option("sqlalchemy.url")
    context.configure(
        url=url,
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
    )

    with context.begin_transaction():
        context.run_migrations()

def do_run_migrations(connection):
    """This helper function runs the actual migrations in a sync context."""
    context.configure(connection=connection, target_metadata=target_metadata)

    with context.begin_transaction():
        context.run_migrations()

def run_migrations_online() -> None:
    """Run migrations in 'online' mode using an AsyncEngine."""
    # 1. Create an AsyncEngine instead of a sync engine
    connectable = async_engine_from_config(
        config.get_section(config.config_ini_section, {}),
        prefix="sqlalchemy.",
        poolclass=pool.NullPool,
    )

    # 2. Define an async execution routine
    async def run_async():
        async with connectable.connect() as connection:
            # run_sync bridges the async connection into the sync migration runner
            await connection.run_sync(do_run_migrations)

        await connectable.dispose()

    # 3. Execute the async routine using asyncio
    asyncio.run(run_async())


if context.is_offline_mode():
    run_migrations_offline()
else:
    run_migrations_online()
