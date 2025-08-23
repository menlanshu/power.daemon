# PowerDaemon Setup Guide 🚀

**Complete Environment Setup and Testing Guide for PowerDaemon Enterprise Deployment System**

This guide will walk you through setting up a complete PowerDaemon environment from scratch, including all dependencies, services, and testing procedures. Perfect for learning and understanding the entire solution.

## 📋 Table of Contents

1. [Prerequisites](#prerequisites)
2. [Infrastructure Setup](#infrastructure-setup)
3. [Database Setup](#database-setup)
4. [Message Queue Setup](#message-queue-setup)
5. [Cache Setup](#cache-setup)
6. [PowerDaemon Solution Setup](#powerdaemon-solution-setup)
7. [Testing the System](#testing-the-system)
8. [Learning Workflows](#learning-workflows)
9. [Production Configuration](#production-configuration)
10. [Troubleshooting](#troubleshooting)

---

## 🛠️ Prerequisites

### System Requirements
- **Operating System**: Windows 10+ or macOS 11+ or Linux (Ubuntu 20.04+)
- **RAM**: 8GB minimum, 16GB recommended for full testing
- **Disk Space**: 5GB for development environment
- **Network**: Internet access for package downloads

### Required Software

#### 1. .NET 8 SDK
```bash
# Download from: https://dotnet.microsoft.com/download/dotnet/8.0
# Verify installation:
dotnet --version
# Should show: 8.0.x or higher
```

#### 2. Git
```bash
# Windows: Download from https://git-scm.com/
# macOS: brew install git
# Linux: sudo apt install git

# Verify:
git --version
```

#### 3. Docker Desktop
```bash
# Download from: https://www.docker.com/products/docker-desktop/
# This is required for PostgreSQL, RabbitMQ, and Redis

# Verify:
docker --version
docker-compose --version
```

#### 4. IDE (Choose One)
- **Visual Studio 2022** (Windows) - Recommended
- **Visual Studio Code** (Cross-platform) with C# extension
- **JetBrains Rider** (Cross-platform)

---

## 🏗️ Infrastructure Setup

### Step 1: Clone the Repository
```bash
# Clone the PowerDaemon repository
git clone https://github.com/your-org/powerdaemon.git
cd powerdaemon

# Verify the structure
ls -la
# You should see: src/, tests/, Design/, CLAUDE.md, etc.
```

### Step 2: Create Docker Infrastructure
Create a `docker-compose.yml` file in the root directory:

```yaml
version: '3.8'
services:
  # PostgreSQL Database
  postgresql:
    image: postgres:15-alpine
    container_name: powerdaemon-postgres
    environment:
      POSTGRES_DB: powerdaemon
      POSTGRES_USER: powerdaemon
      POSTGRES_PASSWORD: PowerDaemon2024!
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./scripts/init-db.sql:/docker-entrypoint-initdb.d/init-db.sql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U powerdaemon"]
      interval: 10s
      timeout: 5s
      retries: 5

  # RabbitMQ Message Queue
  rabbitmq:
    image: rabbitmq:3.12-management-alpine
    container_name: powerdaemon-rabbitmq
    environment:
      RABBITMQ_DEFAULT_USER: powerdaemon
      RABBITMQ_DEFAULT_PASS: PowerDaemon2024!
      RABBITMQ_DEFAULT_VHOST: powerdaemon
    ports:
      - "5672:5672"    # AMQP port
      - "15672:15672"  # Management UI
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
      - ./scripts/rabbitmq.conf:/etc/rabbitmq/rabbitmq.conf
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "ping"]
      interval: 30s
      timeout: 10s
      retries: 5

  # Redis Cache
  redis:
    image: redis:7-alpine
    container_name: powerdaemon-redis
    command: redis-server --requirepass PowerDaemon2024!
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "--no-auth-warning", "-a", "PowerDaemon2024!", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
  rabbitmq_data:
  redis_data:
```

---

## 🗄️ Database Setup

### Step 1: Create Database Initialization Script
Create `scripts/init-db.sql`:

```sql
-- PowerDaemon Database Initialization
CREATE DATABASE powerdaemon_dev;
CREATE DATABASE powerdaemon_test;

-- Grant permissions
GRANT ALL PRIVILEGES ON DATABASE powerdaemon TO powerdaemon;
GRANT ALL PRIVILEGES ON DATABASE powerdaemon_dev TO powerdaemon;
GRANT ALL PRIVILEGES ON DATABASE powerdaemon_test TO powerdaemon;

-- Create extensions
\c powerdaemon;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\c powerdaemon_dev;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

\c powerdaemon_test;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
```

### Step 2: Start PostgreSQL
```bash
# Start PostgreSQL container
docker-compose up -d postgresql

# Wait for health check
docker-compose ps postgresql

# Test connection
docker exec -it powerdaemon-postgres psql -U powerdaemon -d powerdaemon -c "SELECT version();"
```

### Step 3: Run Database Migrations
```bash
# Navigate to Central service
cd src/PowerDaemon.Central

# Install EF Core tools if not already installed
dotnet tool install --global dotnet-ef

# Create initial migration (if needed)
dotnet ef migrations add InitialCreate

# Apply migrations
dotnet ef database update

# Verify tables were created
docker exec -it powerdaemon-postgres psql -U powerdaemon -d powerdaemon -c "\dt"
```

---

## 📨 Message Queue Setup

### Step 1: Create RabbitMQ Configuration
Create `scripts/rabbitmq.conf`:

```ini
# PowerDaemon RabbitMQ Configuration
management.listener.port = 15672
management.listener.ssl = false

# Performance settings for production scale
vm_memory_high_watermark.relative = 0.7
disk_free_limit.relative = 2.0
cluster_partition_handling = autoheal

# Logging
log.console = true
log.console.level = info
log.file = true
log.file.level = info
```

### Step 2: Start RabbitMQ
```bash
# Start RabbitMQ container
docker-compose up -d rabbitmq

# Wait for startup (takes ~30 seconds)
docker-compose logs rabbitmq | grep "Server startup complete"

# Access Management UI
open http://localhost:15672
# Login: powerdaemon / PowerDaemon2024!
```

### Step 3: Configure Exchanges and Queues
Create `scripts/setup-rabbitmq.sh`:

```bash
#!/bin/bash
# PowerDaemon RabbitMQ Setup Script

# Wait for RabbitMQ to be ready
echo "Waiting for RabbitMQ to start..."
until docker exec powerdaemon-rabbitmq rabbitmq-diagnostics ping; do
    sleep 5
done

# Create exchanges
docker exec powerdaemon-rabbitmq rabbitmqadmin declare exchange name=powerdaemon type=topic durable=true
docker exec powerdaemon-rabbitmq rabbitmqadmin declare exchange name=powerdaemon.dlx type=direct durable=true

# Create queues
docker exec powerdaemon-rabbitmq rabbitmqadmin declare queue name=powerdaemon.deployments durable=true \
    arguments='{"x-dead-letter-exchange":"powerdaemon.dlx","x-message-ttl":3600000}'

docker exec powerdaemon-rabbitmq rabbitmqadmin declare queue name=powerdaemon.commands durable=true \
    arguments='{"x-dead-letter-exchange":"powerdaemon.dlx","x-message-ttl":3600000}'

docker exec powerdaemon-rabbitmq rabbitmqadmin declare queue name=powerdaemon.status durable=true \
    arguments='{"x-dead-letter-exchange":"powerdaemon.dlx","x-message-ttl":3600000}'

# Create bindings
docker exec powerdaemon-rabbitmq rabbitmqadmin declare binding source=powerdaemon \
    destination=powerdaemon.deployments routing_key="deployment.*"

docker exec powerdaemon-rabbitmq rabbitmqadmin declare binding source=powerdaemon \
    destination=powerdaemon.commands routing_key="command.*"

docker exec powerdaemon-rabbitmq rabbitmqadmin declare binding source=powerdaemon \
    destination=powerdaemon.status routing_key="status.*"

echo "RabbitMQ setup complete!"
```

Run the setup:
```bash
chmod +x scripts/setup-rabbitmq.sh
./scripts/setup-rabbitmq.sh
```

---

## 🗃️ Cache Setup

### Step 1: Start Redis
```bash
# Start Redis container
docker-compose up -d redis

# Test Redis connection
docker exec -it powerdaemon-redis redis-cli -a PowerDaemon2024! ping
# Should return: PONG
```

### Step 2: Configure Redis for PowerDaemon
```bash
# Connect to Redis CLI
docker exec -it powerdaemon-redis redis-cli -a PowerDaemon2024!

# Set up basic configuration
CONFIG SET maxmemory 2gb
CONFIG SET maxmemory-policy allkeys-lru
CONFIG REWRITE

# Test basic operations
SET test:key "Hello PowerDaemon"
GET test:key
DEL test:key

# Exit Redis CLI
EXIT
```

---

## 🏢 PowerDaemon Solution Setup

### Step 1: Build the Solution
```bash
# Return to solution root
cd /path/to/powerdaemon

# Restore packages
dotnet restore

# Build all projects
dotnet build

# Build in Release mode for production testing
dotnet build --configuration Release
```

### Step 2: Configure Application Settings

#### Central Service Configuration
Create `src/PowerDaemon.Central/appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "PowerDaemon": "Debug"
    }
  },
  "Database": {
    "Provider": "PostgreSQL",
    "ConnectionString": "Host=localhost;Port=5432;Database=powerdaemon;Username=powerdaemon;Password=PowerDaemon2024!;Include Error Detail=true",
    "AutoMigrateOnStartup": true,
    "EnableSensitiveDataLogging": true
  },
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "powerdaemon",
    "Password": "PowerDaemon2024!",
    "VirtualHost": "powerdaemon",
    "ExchangeName": "powerdaemon",
    "DeploymentQueue": "powerdaemon.deployments",
    "CommandQueue": "powerdaemon.commands",
    "StatusQueue": "powerdaemon.status",
    "MessageTtlSeconds": 3600,
    "MaxRetryAttempts": 3
  },
  "Redis": {
    "ConnectionString": "localhost:6379,password=PowerDaemon2024!",
    "InstanceName": "PowerDaemon-Dev",
    "Database": 0,
    "DefaultTtlHours": 24,
    "KeyPrefix": "pd:dev:"
  },
  "Identity": {
    "ActiveDirectory": {
      "Domain": "local.domain",
      "Server": "localhost",
      "UseStartTls": false,
      "BaseDn": "DC=local,DC=domain"
    },
    "Jwt": {
      "SecretKey": "PowerDaemon2024-SuperSecret-Development-Key-256bits!",
      "Issuer": "PowerDaemon-Dev",
      "Audience": "PowerDaemon-Users",
      "ExpirationMinutes": 60,
      "RefreshExpirationDays": 7,
      "EnableEncryption": false
    }
  },
  "Orchestrator": {
    "MaxConcurrentWorkflows": 5,
    "MaxQueuedWorkflows": 20,
    "WorkflowTimeoutMinutes": 60,
    "EnableAutoRollback": true,
    "MaxRetryAttempts": 3
  }
}
```

#### Web UI Configuration
Create `src/PowerDaemon.Web/appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.SignalR": "Debug",
      "PowerDaemon": "Debug"
    }
  },
  "Database": {
    "ConnectionString": "Host=localhost;Port=5432;Database=powerdaemon;Username=powerdaemon;Password=PowerDaemon2024!",
    "EnableSensitiveDataLogging": true,
    "AutoMigrate": true
  },
  "GrpcService": {
    "Endpoint": "http://localhost:5000"
  },
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "powerdaemon",
    "Password": "PowerDaemon2024!",
    "VirtualHost": "powerdaemon"
  },
  "Redis": {
    "ConnectionString": "localhost:6379,password=PowerDaemon2024!",
    "InstanceName": "PowerDaemon-Web-Dev",
    "Database": 1
  }
}
```

#### Agent Configuration
Create `src/PowerDaemon.Agent/appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "PowerDaemon": "Debug"
    }
  },
  "Agent": {
    "ServerId": null,
    "Hostname": "",
    "CentralServiceUrl": "http://localhost:5000",
    "MetricsCollectionIntervalSeconds": 60,
    "HeartbeatIntervalSeconds": 15,
    "ServiceDiscoveryIntervalSeconds": 300,
    "GrpcEndpoint": "http://localhost:5000",
    "UseTls": false,
    "MaxRetryAttempts": 5,
    "RetryDelaySeconds": 30
  }
}
```

---

## 🧪 Testing the System

### Step 1: Start All Infrastructure Services
```bash
# Start all infrastructure
docker-compose up -d

# Verify all services are healthy
docker-compose ps
# All services should show "Up (healthy)"

# Check logs if any issues
docker-compose logs postgresql
docker-compose logs rabbitmq
docker-compose logs redis
```

### Step 2: Start PowerDaemon Services

#### Terminal 1: Central Service
```bash
cd src/PowerDaemon.Central
dotnet run --environment Development

# Wait for:
# "Now listening on: http://localhost:5000"
# "Application started. Press Ctrl+C to shut down."
```

#### Terminal 2: Web UI Service
```bash
cd src/PowerDaemon.Web
dotnet run --environment Development

# Wait for:
# "Now listening on: https://localhost:5001"
# "Application started. Press Ctrl+C to shut down."
```

#### Terminal 3: Agent Service
```bash
cd src/PowerDaemon.Agent
dotnet run --environment Development

# Watch for:
# "Agent registration successful"
# "Heartbeat sent successfully"
# "Service discovery completed"
```

### Step 3: Access the Web Interface
```bash
# Open your browser to:
https://localhost:5001

# You should see:
# - PowerDaemon Dashboard
# - Server status (1 agent connected)
# - Service discovery results
# - Real-time metrics
```

### Step 4: Run the Test Suite
```bash
# Run unit tests
cd tests/PowerDaemon.Tests.Unit
dotnet test

# Run integration tests (requires Docker services)
cd ../PowerDaemon.Tests.Integration
dotnet test

# Run all tests from solution root
cd ../../
dotnet test --verbosity normal
```

### Step 5: Test Key Features

#### A. Dashboard Testing
1. Navigate to `https://localhost:5001`
2. Verify server appears in dashboard
3. Check real-time updates (refresh every 30 seconds)
4. Verify metrics are being collected

#### B. Service Management Testing
1. Go to Services page (`https://localhost:5001/services`)
2. View discovered services
3. Test service start/stop operations
4. Verify real-time status updates

#### C. Metrics Testing
1. Go to Metrics page (`https://localhost:5001/metrics`)
2. View system metrics (CPU, Memory, Disk, Network)
3. Test time filtering
4. Verify data updates every minute

#### D. Deployment Testing (Advanced)
```bash
# Test deployment command via API
curl -X POST https://localhost:5001/api/deployments \
  -H "Content-Type: application/json" \
  -d '{
    "serviceName": "TestService",
    "version": "1.0.0",
    "strategy": "Rolling",
    "targetServers": ["agent-001"]
  }'
```

---

## 📚 Learning Workflows

### Beginner Workflow (1-2 hours)
1. **Setup Environment** (30 min)
   - Install prerequisites
   - Start Docker services
   - Build solution

2. **Basic Testing** (30 min)
   - Start Central service
   - Start one Agent
   - Access Web UI
   - Explore Dashboard

3. **Service Discovery** (30 min)
   - View discovered services
   - Understand service metadata
   - Test service operations

### Intermediate Workflow (3-4 hours)
1. **Complete System** (1 hour)
   - Start all services including Web UI
   - Configure multiple agents (simulate multiple servers)
   - Explore all UI features

2. **Message Queue Testing** (1 hour)
   - Access RabbitMQ Management UI
   - Send test messages
   - Monitor queue operations
   - Test dead letter handling

3. **Database Exploration** (1 hour)
   - Connect to PostgreSQL
   - Explore database schema
   - Understand data relationships
   - Monitor real-time data changes

4. **Testing Suite** (1 hour)
   - Run comprehensive test suite
   - Understand test coverage
   - Analyze test results

### Advanced Workflow (Full Day)
1. **Production Simulation** (2 hours)
   - Configure production settings
   - Simulate multiple servers (Docker containers)
   - Test load scenarios
   - Monitor performance metrics

2. **Security Testing** (2 hours)
   - Configure Active Directory (if available)
   - Test JWT authentication
   - Verify authorization policies
   - Test security scenarios

3. **Deployment Orchestration** (2 hours)
   - Test Blue-Green deployments
   - Test Canary deployments
   - Test Rolling deployments
   - Monitor deployment workflows

4. **Monitoring & Alerting** (2 hours)
   - Configure alert rules
   - Test notification channels
   - Create custom dashboards
   - Analyze system metrics

---

## 🏭 Production Configuration

### Production Environment Variables
Create `.env.production`:

```bash
# Database
DATABASE_CONNECTION_STRING="Host=prod-postgres;Database=powerdaemon;Username=powerdaemon;Password=STRONG_PASSWORD_HERE"

# RabbitMQ
RABBITMQ_HOST="prod-rabbitmq-cluster"
RABBITMQ_USERNAME="powerdaemon"
RABBITMQ_PASSWORD="STRONG_PASSWORD_HERE"

# Redis
REDIS_CONNECTION_STRING="prod-redis-cluster:6379,password=STRONG_PASSWORD_HERE"

# Active Directory
AD_DOMAIN="company.com"
AD_SERVER="ad.company.com"
AD_USERNAME="powerdaemon-service"
AD_PASSWORD="STRONG_PASSWORD_HERE"

# JWT
JWT_SECRET_KEY="SUPER_STRONG_256_BIT_KEY_FOR_PRODUCTION_USE_ONLY"

# SSL/TLS
SSL_CERT_PATH="/etc/ssl/certs/powerdaemon.crt"
SSL_KEY_PATH="/etc/ssl/private/powerdaemon.key"
```

### Production Docker Compose
Create `docker-compose.prod.yml`:

```yaml
version: '3.8'
services:
  powerdaemon-central:
    build: 
      context: .
      dockerfile: src/PowerDaemon.Central/Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=https://+:443;http://+:80
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./certs:/etc/ssl/certs:ro
    depends_on:
      - postgresql
      - rabbitmq
      - redis

  powerdaemon-web:
    build:
      context: .
      dockerfile: src/PowerDaemon.Web/Dockerfile
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=https://+:443;http://+:80
    ports:
      - "8080:80"
      - "8443:443"
    depends_on:
      - powerdaemon-central

  # ... other services
```

---

## 🔧 Troubleshooting

### Common Issues and Solutions

#### 1. Database Connection Issues
```bash
# Check PostgreSQL status
docker-compose logs postgresql

# Test connection manually
docker exec -it powerdaemon-postgres psql -U powerdaemon -d powerdaemon

# Reset database if needed
docker-compose down
docker volume rm powerdaemon_postgres_data
docker-compose up -d postgresql
```

#### 2. RabbitMQ Connection Issues
```bash
# Check RabbitMQ logs
docker-compose logs rabbitmq

# Verify queues exist
docker exec powerdaemon-rabbitmq rabbitmqctl list_queues

# Reset RabbitMQ if needed
docker-compose restart rabbitmq
./scripts/setup-rabbitmq.sh
```

#### 3. Redis Connection Issues
```bash
# Check Redis status
docker-compose logs redis

# Test Redis connection
docker exec powerdaemon-redis redis-cli -a PowerDaemon2024! ping

# Check Redis memory usage
docker exec powerdaemon-redis redis-cli -a PowerDaemon2024! info memory
```

#### 4. Agent Registration Issues
```bash
# Check Central service logs for gRPC errors
cd src/PowerDaemon.Central
dotnet run --environment Development

# Check Agent logs
cd src/PowerDaemon.Agent
dotnet run --environment Development

# Verify network connectivity
curl http://localhost:5000/health
```

#### 5. Build Issues
```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build

# Check for missing packages
dotnet list package --outdated
```

### Performance Tuning

#### Database Optimization
```sql
-- Connect to PostgreSQL
docker exec -it powerdaemon-postgres psql -U powerdaemon -d powerdaemon

-- Create indexes for better performance
CREATE INDEX IF NOT EXISTS idx_servers_hostname ON "Servers"("Hostname");
CREATE INDEX IF NOT EXISTS idx_services_server_id ON "Services"("ServerId");
CREATE INDEX IF NOT EXISTS idx_metrics_timestamp ON "Metrics"("Timestamp");
CREATE INDEX IF NOT EXISTS idx_metrics_server_id ON "Metrics"("ServerId");

-- Analyze tables
ANALYZE;
```

#### Memory Configuration
```bash
# Increase Docker memory limits if needed
# Docker Desktop > Settings > Resources > Memory > 8GB+

# Monitor container resource usage
docker stats powerdaemon-postgres powerdaemon-rabbitmq powerdaemon-redis
```

### Logs and Monitoring

#### View Application Logs
```bash
# Central Service logs
cd src/PowerDaemon.Central
dotnet run | tee logs/central.log

# Agent logs
cd src/PowerDaemon.Agent
dotnet run | tee logs/agent.log

# Web UI logs
cd src/PowerDaemon.Web
dotnet run | tee logs/web.log
```

#### Infrastructure Logs
```bash
# All infrastructure logs
docker-compose logs -f

# Specific service logs
docker-compose logs -f postgresql
docker-compose logs -f rabbitmq
docker-compose logs -f redis
```

---

## 📞 Support and Resources

### Documentation
- **Architecture**: `Design/PowerDaemon.md`
- **Phase 3 Design**: `Design/Phase3-Architecture.md`
- **API Documentation**: Available at `https://localhost:5001/swagger` (when Web UI is running)
- **Test Reports**: `tests/TEST_REPORT.md`

### Development Tools
- **Database GUI**: pgAdmin, DBeaver, or DataGrip for PostgreSQL
- **Message Queue UI**: RabbitMQ Management UI at `http://localhost:15672`
- **Redis GUI**: RedisInsight or Redis Commander
- **API Testing**: Postman, Insomnia, or curl

### Health Checks
- **Central Service**: `http://localhost:5000/health`
- **Web UI**: `https://localhost:5001/healthz`
- **Database**: `docker exec powerdaemon-postgres pg_isready -U powerdaemon`
- **RabbitMQ**: `http://localhost:15672` (Management UI)
- **Redis**: `docker exec powerdaemon-redis redis-cli -a PowerDaemon2024! ping`

---

**🎉 Congratulations!** You now have a complete PowerDaemon environment ready for learning and testing. Start with the Beginner Workflow and gradually work your way up to production scenarios.

For questions or issues, check the troubleshooting section or refer to the comprehensive documentation in the `Design/` folder.

**Happy Learning! 🚀**