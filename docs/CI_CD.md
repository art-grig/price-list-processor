# CI/CD Setup Guide

This document explains the GitHub Actions workflows for building and testing the Price List Processor.

## 🚀 Workflows

### 1. Basic Workflow (`dotnet.yml`)

**Purpose**: Simple build and test workflow for quick feedback

**Features**:
- Runs on push/PR to master/main
- Builds the solution
- Starts Redis + MinIO via Docker Compose
- Runs 26 tests first, then E2E test separately
- Publishes artifacts on main branch

**Usage**:
```yaml
# Triggers on:
- Push to master/main
- Pull requests to master/main
```

### 2. Advanced Workflow (`dotnet-advanced.yml`)

**Purpose**: Comprehensive testing with better isolation and reporting

**Features**:
- Separate jobs for unit, integration, and E2E tests
- Test result artifacts and reporting
- Better error isolation
- Caching for faster builds
- Health checks for services

**Usage**:
```yaml
# Triggers on:
- Push to master/main/develop
- Pull requests to master/main
- Manual workflow dispatch
```

## 🐳 Docker Services

### Test Infrastructure (`docker-compose.tests.yml`)

**Services**:
- **Redis**: Port 6379 - For Hangfire job storage
- **MinIO**: Ports 9000 (API), 9001 (Console) - For S3-compatible storage

**Health Checks**:
- Redis: `redis-cli ping`
- MinIO: HTTP health endpoint

## 📊 Test Execution Strategy

### Test Separation

1. **Unit Tests** (Fast, no dependencies)
   - Run first for quick feedback
   - No external services required
   - Filter: `Category=Unit`

2. **Integration Tests** (26 tests)
   - Require Redis + MinIO
   - Run after unit tests pass
   - Filter: `FullyQualifiedName!~EndToEndEmailProcessingTests&Category=Integration`

3. **E2E Tests** (1 test)
   - Run separately for isolation
   - Requires clean Redis + MinIO state
   - Filter: `FullyQualifiedName~EndToEndEmailProcessingTests`

### Why Separate E2E Tests?

- **Isolation**: E2E tests need clean state
- **Reliability**: Prevents interference from other tests
- **Debugging**: Easier to identify E2E-specific issues
- **Performance**: Can run E2E tests with different timeouts

## 🔧 Local Development

### Running Tests Locally

```bash
# Start test infrastructure
docker-compose -f docker-compose.tests.yml up -d

# Run unit tests
dotnet test --filter "Category=Unit"

# Run integration tests (excluding E2E)
dotnet test --filter "FullyQualifiedName!~EndToEndEmailProcessingTests"

# Run E2E test separately
dotnet test --filter "FullyQualifiedName~EndToEndEmailProcessingTests"

# Stop infrastructure
docker-compose -f docker-compose.tests.yml down
```

### Test Categories

Add categories to your tests:

```csharp
[Trait("Category", TestCategories.Unit)]
public class MyUnitTest { }

[Trait("Category", TestCategories.Integration)]
public class MyIntegrationTest { }
```

## 📈 Monitoring and Debugging

### Test Results

- **Artifacts**: Test results are uploaded as artifacts
- **Logs**: Detailed console output for debugging
- **TRX Files**: Visual Studio compatible test results

### Common Issues

1. **Service Startup Timeout**
   - Increase `wait-for-tcp-timeout` in workflow
   - Check service health checks

2. **Test Isolation Issues**
   - Ensure E2E test runs with clean state
   - Check Redis/MinIO data persistence

3. **Build Failures**
   - Check .NET version compatibility
   - Verify package restore

### Debug Commands

```bash
# Check service health
docker-compose -f docker-compose.tests.yml ps

# View service logs
docker-compose -f docker-compose.tests.yml logs redis
docker-compose -f docker-compose.tests.yml logs minio

# Test Redis connection
docker exec -it $(docker-compose -f docker-compose.tests.yml ps -q redis) redis-cli ping

# Test MinIO connection
curl http://localhost:9000/minio/health/live
```

## 🚀 Deployment

### Build Artifacts

- **Location**: `./publish/` directory
- **Retention**: 30 days
- **Trigger**: Only on main/master branch

### Production Deployment

Use the build artifacts to deploy to your production environment:

```bash
# Download artifacts from GitHub Actions
# Deploy to your target environment
```

## 📝 Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DOTNET_VERSION` | .NET version to use | `8.0.x` |
| `TEST_TIMEOUT` | Test timeout | `00:10:00` |

### Workflow Customization

To modify the workflows:

1. **Add new test categories**: Update `TestCategories.cs`
2. **Change service ports**: Update `docker-compose.tests.yml`
3. **Modify test filters**: Update workflow filter expressions
4. **Add new environments**: Create new workflow files

## 🔒 Security

### Secrets

Store sensitive information in GitHub Secrets:

- Database connection strings
- API keys
- Service credentials

### Best Practices

- Use least privilege for service accounts
- Rotate secrets regularly
- Monitor for exposed credentials in logs

## 📚 Additional Resources

- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [.NET Testing Documentation](https://docs.microsoft.com/en-us/dotnet/core/testing/)
- [Hangfire Testing](https://docs.hangfire.io/en/latest/background-processing/testing.html)
