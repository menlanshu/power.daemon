#!/bin/bash
# PowerDaemon RabbitMQ Setup Script
# This script configures RabbitMQ with the required exchanges, queues, and bindings

set -e  # Exit on any error

echo "🐰 Setting up RabbitMQ for PowerDaemon..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
CONTAINER_NAME="powerdaemon-rabbitmq"
VHOST="powerdaemon"
USERNAME="powerdaemon"
EXCHANGE_NAME="powerdaemon"
DLX_NAME="powerdaemon.dlx"

# Function to wait for RabbitMQ to be ready
wait_for_rabbitmq() {
    echo -e "${YELLOW}Waiting for RabbitMQ to start...${NC}"
    local attempts=0
    local max_attempts=30
    
    while ! docker exec $CONTAINER_NAME rabbitmq-diagnostics ping >/dev/null 2>&1; do
        attempts=$((attempts + 1))
        if [ $attempts -gt $max_attempts ]; then
            echo -e "${RED}❌ RabbitMQ failed to start after $max_attempts attempts${NC}"
            exit 1
        fi
        echo -e "${BLUE}Attempt $attempts/$max_attempts - waiting for RabbitMQ...${NC}"
        sleep 5
    done
    
    echo -e "${GREEN}✅ RabbitMQ is ready!${NC}"
}

# Function to execute RabbitMQ admin command
rabbitmq_admin() {
    docker exec $CONTAINER_NAME rabbitmqadmin "$@"
}

# Function to execute RabbitMQ control command
rabbitmq_ctl() {
    docker exec $CONTAINER_NAME rabbitmqctl "$@"
}

# Wait for RabbitMQ to be ready
wait_for_rabbitmq

echo -e "${BLUE}📊 Current RabbitMQ status:${NC}"
rabbitmq_ctl status | grep "RabbitMQ version"

# Create virtual host if it doesn't exist
echo -e "${YELLOW}🏠 Setting up virtual host: $VHOST${NC}"
rabbitmq_ctl add_vhost $VHOST || echo "Virtual host $VHOST already exists"

# Set permissions for the user on the virtual host
echo -e "${YELLOW}👤 Setting permissions for user: $USERNAME${NC}"
rabbitmq_ctl set_permissions -p $VHOST $USERNAME ".*" ".*" ".*"

# Create main exchange
echo -e "${YELLOW}📤 Creating main exchange: $EXCHANGE_NAME${NC}"
rabbitmq_admin -V $VHOST declare exchange name=$EXCHANGE_NAME type=topic durable=true

# Create dead letter exchange
echo -e "${YELLOW}💀 Creating dead letter exchange: $DLX_NAME${NC}"
rabbitmq_admin -V $VHOST declare exchange name=$DLX_NAME type=direct durable=true

# Create dead letter queue
echo -e "${YELLOW}📥 Creating dead letter queue${NC}"
rabbitmq_admin -V $VHOST declare queue name="$DLX_NAME.queue" durable=true

# Bind dead letter queue to DLX
rabbitmq_admin -V $VHOST declare binding source=$DLX_NAME destination="$DLX_NAME.queue" routing_key=""

# Define queues with their properties
declare -A QUEUES=(
    ["powerdaemon.deployments"]="deployment.*"
    ["powerdaemon.commands"]="command.*"
    ["powerdaemon.status"]="status.*"
    ["powerdaemon.alerts"]="alert.*"
    ["powerdaemon.metrics"]="metrics.*"
    ["powerdaemon.workflows"]="workflow.*"
)

# Create queues and bindings
for queue in "${!QUEUES[@]}"; do
    routing_key=${QUEUES[$queue]}
    
    echo -e "${YELLOW}📋 Creating queue: $queue${NC}"
    
    # Create queue with dead letter exchange and TTL
    rabbitmq_admin -V $VHOST declare queue name="$queue" durable=true \
        arguments="{\"x-dead-letter-exchange\":\"$DLX_NAME\",\"x-message-ttl\":3600000,\"x-max-length\":10000}"
    
    # Create binding
    echo -e "${BLUE}🔗 Binding queue $queue with routing key: $routing_key${NC}"
    rabbitmq_admin -V $VHOST declare binding source=$EXCHANGE_NAME destination="$queue" routing_key="$routing_key"
done

# Create additional specialized queues for production
echo -e "${YELLOW}🏭 Creating production-scale queues${NC}"

# High-priority queue for critical operations
rabbitmq_admin -V $VHOST declare queue name="powerdaemon.priority" durable=true \
    arguments="{\"x-dead-letter-exchange\":\"$DLX_NAME\",\"x-message-ttl\":1800000,\"x-max-priority\":10}"

rabbitmq_admin -V $VHOST declare binding source=$EXCHANGE_NAME destination="powerdaemon.priority" routing_key="priority.*"

# Batch processing queue
rabbitmq_admin -V $VHOST declare queue name="powerdaemon.batch" durable=true \
    arguments="{\"x-dead-letter-exchange\":\"$DLX_NAME\",\"x-message-ttl\":7200000}"

rabbitmq_admin -V $VHOST declare binding source=$EXCHANGE_NAME destination="powerdaemon.batch" routing_key="batch.*"

# System monitoring queue
rabbitmq_admin -V $VHOST declare queue name="powerdaemon.monitoring" durable=true \
    arguments="{\"x-dead-letter-exchange\":\"$DLX_NAME\",\"x-message-ttl\":900000}"

rabbitmq_admin -V $VHOST declare binding source=$EXCHANGE_NAME destination="powerdaemon.monitoring" routing_key="monitoring.*"

# Set policies for high availability (if running in cluster)
echo -e "${YELLOW}📋 Setting policies for high availability${NC}"
rabbitmq_ctl -p $VHOST set_policy ha-powerdaemon "powerdaemon.*" '{"ha-mode":"exactly","ha-params":2,"ha-sync-mode":"automatic"}'

# Configure queue mirroring for production
rabbitmq_ctl -p $VHOST set_policy mirror-powerdaemon "powerdaemon.*" '{"ha-mode":"all","ha-sync-mode":"automatic"}'

# Display final status
echo -e "${GREEN}✅ RabbitMQ setup completed successfully!${NC}"
echo -e "${BLUE}📊 Final configuration:${NC}"

echo -e "${BLUE}Exchanges:${NC}"
rabbitmq_admin -V $VHOST list exchanges name type

echo -e "${BLUE}Queues:${NC}"
rabbitmq_admin -V $VHOST list queues name messages consumers

echo -e "${BLUE}Bindings:${NC}"
rabbitmq_admin -V $VHOST list bindings source destination routing_key

echo -e "${GREEN}🎉 RabbitMQ is ready for PowerDaemon!${NC}"
echo -e "${BLUE}Management UI: http://localhost:15672${NC}"
echo -e "${BLUE}Username: $USERNAME${NC}"
echo -e "${BLUE}Virtual Host: $VHOST${NC}"