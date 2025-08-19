# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PowerDaemon is an enterprise-grade distributed monitoring and deployment system designed to manage exactly 200 servers with 1,600-2,000 services (8-10 services per server) across Windows and Linux environments. 

**Current Status**: Phase 2 (Service Management & Web UI) completed. Full web interface with real-time monitoring, service management, and SignalR integration implemented.

## Architecture

The system follows a multi-tier architecture with these core components:

**Core Projects:**
- **PowerDaemon.Agent**: Cross-platform service agent (Windows Service/Linux Daemon) ✅ **IMPLEMENTED**
- **PowerDaemon.Central**: ASP.NET Core Web API with gRPC server ✅ **IMPLEMENTED**
- **PowerDaemon.Web**: Blazor Server web interface ✅ **IMPLEMENTED**
- **PowerDaemon.Shared**: Common models, DTOs, and configuration ✅ **IMPLEMENTED**
- **PowerDaemon.Protos**: gRPC protocol definitions ✅ **IMPLEMENTED**

**Key Architecture Patterns:**
- Repository Pattern for data access abstraction
- Service Pattern for business logic encapsulation  
- Observer Pattern for real-time updates via SignalR
- Command Pattern for service operations
- Strategy Pattern for database provider switching (PostgreSQL implemented, Oracle planned)

## Technology Stack

- **Backend**: .NET 8, ASP.NET Core Web API ✅
- **Frontend**: Blazor Server with SignalR ✅ **IMPLEMENTED**
- **Database**: PostgreSQL (implemented), Oracle (planned)
- **Message Queue**: RabbitMQ (planned for Phase 3)
- **Cache**: Redis (planned for Phase 3)
- **Communication**: gRPC (agent-central) ✅, REST (web-api) ✅, SignalR (real-time) ✅
- **Authentication**: Active Directory (planned for Phase 3)

## Development Commands

**Build Commands:**
- Build entire solution: `dotnet build`
- Build specific project: `dotnet build src/PowerDaemon.Agent`
- Run Agent: `dotnet run --project src/PowerDaemon.Agent`
- Run Central service: `dotnet run --project src/PowerDaemon.Central`
- Run Web UI: `dotnet run --project src/PowerDaemon.Web`

**Database Commands:**
- Create migration: `cd src/PowerDaemon.Central && dotnet ef migrations add MigrationName`
- Update database: `cd src/PowerDaemon.Central && dotnet ef database update`
- Drop database: `cd src/PowerDaemon.Central && dotnet ef database drop`

**Testing:**
- Run tests: `dotnet test` (when test projects are added)

## Implementation Status

### ✅ **Phase 1: Core Foundation (COMPLETED)**

**Agent Implementation:**
- Cross-platform Windows Service/Linux systemd daemon
- Service discovery for C# applications via Service Control Manager (Windows) and systemd (Linux)
- Comprehensive metrics collection: CPU, Memory, Disk I/O, Network I/O (5-minute intervals)
- Local metric buffering with 30-day retention and automatic cleanup
- gRPC client with automatic registration, heartbeat (30s), and streaming
- Memory optimized: <50MB footprint target

**Central Service Implementation:**
- ASP.NET Core Web API with gRPC server
- PostgreSQL database with Entity Framework Core
- Agent management: registration, heartbeat tracking, command distribution
- Service registry: metadata storage for discovered services
- Metrics ingestion and storage with time-series data
- Health checks and structured logging

**Database Schema (EF Core Migrations):**
- **Servers**: Agent registration and status tracking
- **Services**: Discovered C# service metadata and status
- **ServiceTypes**: TypeA-D classification with configuration templates
- **Deployments**: Version history and rollback tracking (schema ready)
- **Metrics**: Time-series data with 5-minute granularity, 30-day retention

### ✅ **Phase 2: Service Management & Web UI (COMPLETED)**

**Web Interface Implementation:**
- Blazor Server application with responsive design
- SignalR real-time communication for live updates
- Dashboard with server/service status overview
- Service management interface with start/stop/restart operations
- Metrics visualization with time-based filtering
- Server details and monitoring interface
- Health checks UI integration

**Key Features:**
- **Real-time Dashboard**: Live server status, service counts, alerts, and system uptime
- **Service Management**: Full CRUD operations with filtering, search, and command execution
- **Metrics Display**: System metrics visualization with CPU, memory, disk, network monitoring
- **Server Monitoring**: Detailed server information with heartbeat tracking
- **SignalR Integration**: Real-time notifications for status changes and alerts
- **Responsive Design**: Mobile-friendly interface using CSS Grid
- **gRPC Integration**: Service command execution through Central service

**Technical Implementation:**
- Service management via `IServiceManagementService` with gRPC communication
- Real-time notifications via `IRealTimeNotificationService` and SignalR hubs
- Shared database context with Central service for live data access
- Comprehensive error handling and user feedback
- Health checks dashboard integration

### 🚧 **Phase 3: Advanced Deployment & Scale (PLANNED)**
- Binary deployment orchestration
- Blue-green, canary, and rolling deployment strategies
- RabbitMQ message queuing for high-throughput operations
- Redis caching layer for performance optimization
- Active Directory authentication integration
- Role-based access control (RBAC)
- Advanced monitoring and alerting
- Performance optimization for 200-server scale

## Service Discovery ✅

**Implemented Features:**
- **Windows**: Complete Service Control Manager integration with C# service detection
- **Linux**: Full systemd integration with C# binary detection
- **Cross-platform**: Unified service interface with filtering support
- **Service Metadata**: Executable path, version, status, process info, startup configuration
- **Automatic Detection**: Distinguishes C# services from system services using multiple methods

## Metrics Collection ✅

**System Metrics (5-minute intervals):**
- **Windows**: Performance counters for CPU, memory, disk, network
- **Linux**: /proc filesystem parsing for comprehensive system stats
- **Service Metrics**: Per-process memory, thread count, handle count (Windows)
- **Local Buffering**: 30-day retention with automatic cleanup
- **Streaming**: Efficient gRPC streaming to central service

## Communication Protocols ✅

- **gRPC**: Agent-Central communication with TLS support, 4MB message limits
- **Protocol Buffers**: Optimized message formats for registration, heartbeat, metrics, service discovery
- **Health Checks**: PostgreSQL connectivity monitoring
- **REST API**: Central service endpoints (basic structure ready)
- **Error Handling**: Comprehensive retry logic and connection management

## Configuration

**Agent Configuration (`appsettings.json`):**
```json
{
  "Agent": {
    "ServerId": null,
    "Hostname": "",
    "CentralServiceUrl": "https://localhost:5001",
    "MetricsCollectionIntervalSeconds": 300,
    "HeartbeatIntervalSeconds": 30,
    "ServiceDiscoveryIntervalSeconds": 600,
    "GrpcEndpoint": "https://localhost:5001",
    "UseTls": true
  }
}
```

**Central Configuration (`appsettings.json`):**
```json
{
  "Database": {
    "Provider": "PostgreSQL",
    "ConnectionString": "Host=localhost;Database=powerdaemon;Username=postgres;Password=password",
    "AutoMigrateOnStartup": true
  }
}
```

**Web UI Configuration (`appsettings.json`):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=powerdaemon;Username=postgres;Password=password"
  },
  "Database": {
    "ConnectionString": "Host=localhost;Database=powerdaemon;Username=postgres;Password=password",
    "EnableSensitiveDataLogging": true,
    "AutoMigrate": true
  },
  "GrpcService": {
    "Endpoint": "http://localhost:5000"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore.SignalR": "Debug",
      "PowerDaemon": "Debug"
    }
  }
}
```

## Development Workflow

**Starting the System:**
1. Ensure PostgreSQL is running
2. Start Central service: `dotnet run --project src/PowerDaemon.Central`
3. Start Web UI: `dotnet run --project src/PowerDaemon.Web` (runs on https://localhost:5003)
4. Start Agent(s): `dotnet run --project src/PowerDaemon.Agent`
5. Monitor logs for registration and heartbeat confirmation
6. Access web interface at https://localhost:5003 for real-time monitoring

**Key Implementation Files:**

**Agent Layer:**
- `src/PowerDaemon.Agent/Services/ServiceDiscoveryService.cs` - Cross-platform service discovery
- `src/PowerDaemon.Agent/Services/MetricsCollectorService.cs` - System metrics collection  
- `src/PowerDaemon.Agent/Services/GrpcClientService.cs` - Agent gRPC client

**Central Service Layer:**
- `src/PowerDaemon.Central/Services/AgentServiceImplementation.cs` - Central gRPC server
- `src/PowerDaemon.Central/Data/PowerDaemonContext.cs` - EF Core database context

**Web UI Layer:**
- `src/PowerDaemon.Web/Components/Pages/Home.razor` - Real-time dashboard
- `src/PowerDaemon.Web/Components/Pages/Services.razor` - Service management interface
- `src/PowerDaemon.Web/Components/Pages/Metrics.razor` - Metrics visualization
- `src/PowerDaemon.Web/Hubs/DashboardHub.cs` - SignalR hub for real-time updates
- `src/PowerDaemon.Web/Services/ServiceManagementService.cs` - gRPC service management client
- `src/PowerDaemon.Web/Services/RealTimeNotificationService.cs` - SignalR notification service

## Key Design Documents

- **Design/PowerDaemon.md**: Comprehensive 3000+ line design document with full system specifications
- **src/PowerDaemon.Protos/Protos/agent_service.proto**: gRPC service definitions and message contracts

## Web Interface Features ✅

**Dashboard (`/`):**
- Real-time server status overview with live connection indicators
- System statistics: server count, service count, alerts, uptime
- Server cards with status badges and heartbeat tracking
- Recent events feed with system notifications

**Service Management (`/services`):**  
- Comprehensive service listing with filtering and search
- Service status indicators and metadata display
- Service control operations: start, stop, restart with gRPC integration
- Service details modal with complete configuration information
- Real-time status updates via SignalR

**Server Monitoring (`/servers`):**
- Detailed server information cards with OS details
- Hardware specifications: CPU cores, memory, IP addresses
- Agent version tracking and environment classification
- Server health status with visual indicators

**Metrics Dashboard (`/metrics`):**
- System metrics visualization with time-range filtering
- CPU, memory, disk, and network usage displays
- Server-specific and metric-type filtering
- Recent metrics table with real-time updates

**Additional Features:**
- Health Checks UI (`/healthchecksui`) - System health monitoring
- Deployment Management (`/deployments`) - Placeholder for Phase 3
- Responsive design with mobile support
- SignalR real-time updates across all interfaces

## Important Technical Notes

**Protocol Buffer Message Naming:**
- `AgentConfiguration` (protobuf) renamed to `AgentSettings` to avoid namespace collision
- This improves maintainability and eliminates ambiguity with `PowerDaemon.Shared.Configuration.AgentConfiguration`

**Performance Considerations:**
- SignalR connection management for real-time updates
- Database query optimization for 200-server scale
- Efficient gRPC communication with 4MB message limits
- Client-side state management for responsive UI

**Security Foundation:**
- gRPC TLS support (configurable)
- Health checks for system monitoring
- Structured logging with Serilog
- Error handling and user feedback systems

This system is production-ready for Phase 2 requirements with full web interface, real-time monitoring, service management capabilities, and scalable architecture supporting 200 servers with 1,600-2,000 services. Ready for Phase 3 advanced deployment features.