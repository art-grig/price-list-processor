using PriceListProcessor.Core.Interfaces;
using PriceListProcessor.Core.Models;
using System.Collections.Concurrent;

namespace PriceListProcessor.Tests.Mocks;

public class MockApiClient : IApiClient
{
    private static readonly ConcurrentDictionary<string, List<ApiCallRecord>> _testCalls = new();
    private static readonly ConcurrentDictionary<string, Func<ApiRequest, ApiResponse>> _responseProviders = new();
    private static readonly ConcurrentDictionary<string, bool> _shouldFail = new();

    public async Task<ApiResponse> SendDataAsync(ApiRequest request, CancellationToken cancellationToken = default)
    {
        var testId = GetTestIdFromRequest(request);
        
        // Record the call
        var callRecord = new ApiCallRecord
        {
            Request = request,
            Timestamp = DateTime.UtcNow,
            TestId = testId
        };

        _testCalls.AddOrUpdate(testId, 
            new List<ApiCallRecord> { callRecord },
            (key, existing) => { existing.Add(callRecord); return existing; });

        // Simulate async operation
        await Task.Delay(10, cancellationToken);

        // Check if this test should fail
        if (_shouldFail.GetValueOrDefault(testId, false))
        {
            return new ApiResponse
            {
                Success = false,
                Message = $"Mock API failure for test {testId}"
            };
        }

        // Use custom response provider if available
        if (_responseProviders.TryGetValue(testId, out var responseProvider))
        {
            return responseProvider(request);
        }

        // Default successful response
        return new ApiResponse
        {
            Success = true,
            Message = "Mock API call successful",
            Data = new
            {
                processedRows = request.Data.Count,
                fileName = request.FileName,
                isLast = request.IsLast,
                testId = testId,
                timestamp = DateTime.UtcNow
            }
        };
    }

    public static void SetupTest(string testId, Func<ApiRequest, ApiResponse>? responseProvider = null, bool shouldFail = false)
    {
        ClearTestData(testId);
        
        if (responseProvider != null)
        {
            _responseProviders[testId] = responseProvider;
        }
        
        _shouldFail[testId] = shouldFail;
    }

    public static List<ApiCallRecord> GetCallsForTest(string testId)
    {
        return _testCalls.GetValueOrDefault(testId, new List<ApiCallRecord>());
    }

    public static void ClearTestData(string testId)
    {
        _testCalls.TryRemove(testId, out _);
        _responseProviders.TryRemove(testId, out _);
        _shouldFail.TryRemove(testId, out _);
    }

    public static void ClearAllTestData()
    {
        _testCalls.Clear();
        _responseProviders.Clear();
        _shouldFail.Clear();
    }

    public static int GetTotalCallCount()
    {
        return _testCalls.Values.Sum(calls => calls.Count);
    }

    public static Dictionary<string, int> GetCallCountsByTest()
    {
        return _testCalls.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
    }

    public static List<ApiCallRecord> GetLastBatchCallsForTest(string testId)
    {
        var calls = GetCallsForTest(testId);
        return calls.Where(c => c.Request.IsLast).ToList();
    }

    public static bool HasReceivedLastBatchForTest(string testId)
    {
        var calls = GetCallsForTest(testId);
        return calls.Any(c => c.Request.IsLast);
    }

    public static List<ApiCallRecord> GetCallsInOrder(string testId)
    {
        var calls = GetCallsForTest(testId);
        return calls.OrderBy(c => c.Timestamp).ToList();
    }

    private static string GetTestIdFromRequest(ApiRequest request)
    {
        // Try to extract test ID from filename - format: {name}-test-{testId}.csv
        if (request.FileName.Contains("-test-"))
        {
            var testIndex = request.FileName.IndexOf("-test-");
            if (testIndex >= 0)
            {
                var afterTest = request.FileName.Substring(testIndex + 6); // Skip "-test-"
                var dotIndex = afterTest.IndexOf('.');
                if (dotIndex > 0)
                {
                    return afterTest.Substring(0, dotIndex);
                }
                return afterTest; // No extension found
            }
        }

        if (request.Subject.Contains("[TEST:") && request.Subject.Contains("]"))
        {
            var start = request.Subject.IndexOf("[TEST:") + 6;
            var end = request.Subject.IndexOf("]", start);
            if (end > start)
            {
                return request.Subject.Substring(start, end - start);
            }
        }

        // Fallback to a default test ID
        return "default";
    }
}

public class ApiCallRecord
{
    public ApiRequest Request { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public string TestId { get; set; } = string.Empty;
}
