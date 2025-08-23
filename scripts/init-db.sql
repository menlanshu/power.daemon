-- PowerDaemon Database Initialization Script
-- This script sets up the initial database structure and sample data

-- Create development and test databases
CREATE DATABASE IF NOT EXISTS powerdaemon_dev;
CREATE DATABASE IF NOT EXISTS powerdaemon_test;

-- Switch to main database
\c powerdaemon;

-- Create extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Grant permissions to powerdaemon user
GRANT ALL PRIVILEGES ON DATABASE powerdaemon TO powerdaemon;
GRANT ALL PRIVILEGES ON DATABASE powerdaemon_dev TO powerdaemon;
GRANT ALL PRIVILEGES ON DATABASE powerdaemon_test TO powerdaemon;

-- Create schema for better organization (optional)
CREATE SCHEMA IF NOT EXISTS powerdaemon;
ALTER SCHEMA powerdaemon OWNER TO powerdaemon;

-- Set default search path
ALTER DATABASE powerdaemon SET search_path TO powerdaemon, public;

-- Create sample data (will be populated after EF migrations)
-- This is handled by the application startup

-- Performance optimizations
ALTER SYSTEM SET shared_preload_libraries = 'pg_stat_statements';
ALTER SYSTEM SET max_connections = '200';
ALTER SYSTEM SET shared_buffers = '256MB';
ALTER SYSTEM SET effective_cache_size = '1GB';
ALTER SYSTEM SET work_mem = '4MB';

-- Log settings for development
ALTER SYSTEM SET log_statement = 'all';
ALTER SYSTEM SET log_min_duration_statement = 100;

-- Reload configuration
SELECT pg_reload_conf();

-- Development databases setup
\c powerdaemon_dev;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

\c powerdaemon_test;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Return to main database
\c powerdaemon;

-- Display setup completion
SELECT 'PowerDaemon database initialization completed successfully!' as status;