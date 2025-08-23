#!/bin/bash
# PowerDaemon Development Environment Startup Script
# This script starts all required services in the correct order

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
PURPLE='\033[0;35m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

echo -e "${PURPLE}🚀 Starting PowerDaemon Development Environment${NC}"
echo -e "${BLUE}Root directory: $ROOT_DIR${NC}"

# Function to check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Function to wait for service
wait_for_service() {
    local service_name=$1
    local health_check=$2
    local max_attempts=30
    local attempts=0
    
    echo -e "${YELLOW}⏳ Waiting for $service_name to be ready...${NC}"
    
    while ! eval "$health_check" >/dev/null 2>&1; do
        attempts=$((attempts + 1))
        if [ $attempts -gt $max_attempts ]; then
            echo -e "${RED}❌ $service_name failed to start after $max_attempts attempts${NC}"
            exit 1
        fi
        echo -e "${BLUE}Attempt $attempts/$max_attempts - waiting for $service_name...${NC}"
        sleep 5
    done
    
    echo -e "${GREEN}✅ $service_name is ready!${NC}"
}

# Check prerequisites
echo -e "${YELLOW}🔍 Checking prerequisites...${NC}"

if ! command_exists docker; then
    echo -e "${RED}❌ Docker is not installed or not in PATH${NC}"
    exit 1
fi

if ! command_exists docker-compose; then
    echo -e "${RED}❌ Docker Compose is not installed or not in PATH${NC}"
    exit 1
fi

if ! command_exists dotnet; then
    echo -e "${RED}❌ .NET SDK is not installed or not in PATH${NC}"
    exit 1
fi

echo -e "${GREEN}✅ All prerequisites are available${NC}"

# Change to root directory
cd "$ROOT_DIR"

# Step 1: Start infrastructure services
echo -e "${PURPLE}📦 Step 1: Starting infrastructure services${NC}"

# Stop any existing containers
echo -e "${YELLOW}🛑 Stopping any existing containers...${NC}"
docker-compose down >/dev/null 2>&1 || true

# Start infrastructure
echo -e "${YELLOW}🐘 Starting PostgreSQL...${NC}"
docker-compose up -d postgresql

echo -e "${YELLOW}🐰 Starting RabbitMQ...${NC}"
docker-compose up -d rabbitmq

echo -e "${YELLOW}📦 Starting Redis...${NC}"
docker-compose up -d redis

# Wait for services to be ready
wait_for_service "PostgreSQL" "docker exec powerdaemon-postgres pg_isready -U powerdaemon"
wait_for_service "RabbitMQ" "docker exec powerdaemon-rabbitmq rabbitmq-diagnostics ping"
wait_for_service "Redis" "docker exec powerdaemon-redis redis-cli -a PowerDaemon2024! ping"

# Step 2: Configure infrastructure
echo -e "${PURPLE}⚙️ Step 2: Configuring infrastructure${NC}"

# Setup RabbitMQ
if [ -f "$SCRIPT_DIR/setup-rabbitmq.sh" ]; then
    echo -e "${YELLOW}🐰 Configuring RabbitMQ...${NC}"
    chmod +x "$SCRIPT_DIR/setup-rabbitmq.sh"
    "$SCRIPT_DIR/setup-rabbitmq.sh"
else
    echo -e "${RED}⚠️ RabbitMQ setup script not found, skipping configuration${NC}"
fi

# Step 3: Build and setup .NET solution
echo -e "${PURPLE}🏗️ Step 3: Building .NET solution${NC}"

echo -e "${YELLOW}📦 Restoring NuGet packages...${NC}"
dotnet restore

echo -e "${YELLOW}🔨 Building solution...${NC}"
dotnet build

# Step 4: Run database migrations
echo -e "${PURPLE}🗄️ Step 4: Setting up database${NC}"

echo -e "${YELLOW}🔄 Running Entity Framework migrations...${NC}"
cd "$ROOT_DIR/src/PowerDaemon.Central"

# Install EF tools if not already installed
dotnet tool install --global dotnet-ef >/dev/null 2>&1 || echo "EF tools already installed"

# Run migrations
dotnet ef database update --verbose

cd "$ROOT_DIR"

# Step 5: Run tests to verify setup
echo -e "${PURPLE}🧪 Step 5: Running tests to verify setup${NC}"

echo -e "${YELLOW}🧪 Running unit tests...${NC}"
cd "$ROOT_DIR/tests/PowerDaemon.Tests.Unit"
dotnet test --logger "console;verbosity=minimal" || echo -e "${YELLOW}⚠️ Some unit tests failed, but environment is still usable${NC}"

echo -e "${YELLOW}🔗 Running integration tests...${NC}"
cd "$ROOT_DIR/tests/PowerDaemon.Tests.Integration"
dotnet test --logger "console;verbosity=minimal" || echo -e "${YELLOW}⚠️ Some integration tests failed, but environment is still usable${NC}"

cd "$ROOT_DIR"

# Step 6: Display status and next steps
echo -e "${PURPLE}📊 Step 6: Environment status${NC}"

echo -e "${GREEN}✅ Development environment is ready!${NC}"
echo ""
echo -e "${BLUE}📊 Service Status:${NC}"
docker-compose ps

echo ""
echo -e "${BLUE}🌐 Access Points:${NC}"
echo -e "${GREEN}• RabbitMQ Management: http://localhost:15672${NC}"
echo -e "${GREEN}  Username: powerdaemon${NC}"
echo -e "${GREEN}  Password: PowerDaemon2024!${NC}"
echo ""
echo -e "${GREEN}• Database: localhost:5432${NC}"
echo -e "${GREEN}  Database: powerdaemon${NC}"
echo -e "${GREEN}  Username: powerdaemon${NC}"
echo -e "${GREEN}  Password: PowerDaemon2024!${NC}"
echo ""
echo -e "${GREEN}• Redis: localhost:6379${NC}"
echo -e "${GREEN}  Password: PowerDaemon2024!${NC}"

echo ""
echo -e "${BLUE}🚀 Next Steps:${NC}"
echo -e "${YELLOW}1. Start Central Service:${NC}"
echo -e "   cd src/PowerDaemon.Central && dotnet run"
echo ""
echo -e "${YELLOW}2. Start Web UI (in another terminal):${NC}"
echo -e "   cd src/PowerDaemon.Web && dotnet run"
echo ""
echo -e "${YELLOW}3. Start Agent (in another terminal):${NC}"
echo -e "   cd src/PowerDaemon.Agent && dotnet run"
echo ""
echo -e "${YELLOW}4. Access Web UI:${NC}"
echo -e "   https://localhost:5001"
echo ""

# Create a simple status check script
cat > "$ROOT_DIR/check-status.sh" << 'EOF'
#!/bin/bash
echo "🔍 PowerDaemon Environment Status"
echo "=================================="
echo ""

echo "📦 Infrastructure Services:"
docker-compose ps

echo ""
echo "🌐 Service Health Checks:"
echo -n "PostgreSQL: "
docker exec powerdaemon-postgres pg_isready -U powerdaemon 2>/dev/null && echo "✅ Ready" || echo "❌ Not Ready"

echo -n "RabbitMQ: "
docker exec powerdaemon-rabbitmq rabbitmq-diagnostics ping 2>/dev/null && echo "✅ Ready" || echo "❌ Not Ready"

echo -n "Redis: "
docker exec powerdaemon-redis redis-cli -a PowerDaemon2024! ping 2>/dev/null && echo "✅ Ready" || echo "❌ Not Ready"

echo ""
echo "📊 Quick Stats:"
echo -n "RabbitMQ Queues: "
docker exec powerdaemon-rabbitmq rabbitmqctl -p powerdaemon list_queues 2>/dev/null | wc -l || echo "N/A"

echo -n "Database Tables: "
docker exec powerdaemon-postgres psql -U powerdaemon -d powerdaemon -c "\dt" 2>/dev/null | grep -c "table" || echo "N/A"
EOF

chmod +x "$ROOT_DIR/check-status.sh"

echo -e "${GREEN}📋 Status check script created: ./check-status.sh${NC}"
echo -e "${GREEN}🎉 PowerDaemon development environment is ready!${NC}"