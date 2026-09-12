using AegisScribe.ApiService.Infrastructure.Idempotency;
using AegisScribe.Domain.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace AegisScribe.Tests.Infrastructure;

// A tiny in-memory stand-in rather than a mock: the replay/conflict tests need a store that actually
// round-trips what was saved, not just records that SaveAsync was called with something.
internal sealed class FakeIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<string, IdempotencyRecord> _records = [];

    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken ct) =>
        Task.FromResult(_records.GetValueOrDefault(key));

    public Task SaveAsync(string key, IdempotencyRecord record, CancellationToken ct)
    {
        _records[key] = record;
        return Task.CompletedTask;
    }
}

public class IdempotencyActionFilterTests
{
    private static ActionExecutingContext CreateExecutingContext(string? idempotencyKey, object? viewModel)
    {
        var httpContext = new DefaultHttpContext();
        if (idempotencyKey is not null)
        {
            httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var arguments = new Dictionary<string, object?> { ["viewModel"] = viewModel, ["ct"] = CancellationToken.None };
        return new ActionExecutingContext(actionContext, [], arguments, new object());
    }

    private static ActionExecutedContext CreateExecutedContext(ActionExecutingContext executingContext, IActionResult? result) =>
        new(executingContext, executingContext.Filters, executingContext.Controller) { Result = result };

    [Fact]
    public async Task NoHeader_RunsTheActionNormally_AndNeverTouchesTheStore()
    {
        var store = Substitute.For<IIdempotencyStore>();
        var filter = new IdempotencyActionFilter(store, Substitute.For<ICurrentUser>());
        var context = CreateExecutingContext(idempotencyKey: null, viewModel: new { Name = "A" });
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(CreateExecutedContext(context, new OkObjectResult("ok")));
        });

        Assert.True(nextCalled);
        await store.DidNotReceiveWithAnyArgs().FindAsync(default!, default);
    }

    [Fact]
    public async Task FirstRequest_2xxResult_IsStored()
    {
        var store = new FakeIdempotencyStore();
        var filter = new IdempotencyActionFilter(store, Substitute.For<ICurrentUser>());
        var context = CreateExecutingContext("key-1", new { Name = "A" });

        await filter.OnActionExecutionAsync(context,
            () => Task.FromResult(CreateExecutedContext(context, new ObjectResult(new { Id = 1 }) { StatusCode = 200 })));

        // A second, identical request now replays instead of re-running the action.
        var secondContext = CreateExecutingContext("key-1", new { Name = "A" });
        var secondCallRan = false;
        await filter.OnActionExecutionAsync(secondContext, () =>
        {
            secondCallRan = true;
            return Task.FromResult(CreateExecutedContext(secondContext, new OkObjectResult("should not run")));
        });

        Assert.False(secondCallRan);
        Assert.IsType<ContentResult>(secondContext.Result);
        Assert.Equal(200, ((ContentResult)secondContext.Result!).StatusCode);
    }

    [Fact]
    public async Task FirstRequest_NonSuccessResult_IsNotStored_SoARetryStillRunsTheAction()
    {
        var store = new FakeIdempotencyStore();
        var filter = new IdempotencyActionFilter(store, Substitute.For<ICurrentUser>());
        var context = CreateExecutingContext("key-1", new { Name = "" });

        await filter.OnActionExecutionAsync(context,
            () => Task.FromResult(CreateExecutedContext(context, new ObjectResult(new ProblemDetails()) { StatusCode = 400 })));

        var retryContext = CreateExecutingContext("key-1", new { Name = "Fixed" });
        var retryRan = false;
        await filter.OnActionExecutionAsync(retryContext, () =>
        {
            retryRan = true;
            return Task.FromResult(CreateExecutedContext(retryContext, new ObjectResult(new { Id = 1 }) { StatusCode = 200 }));
        });

        Assert.True(retryRan);
    }

    [Fact]
    public async Task RepeatKey_DifferentBody_Returns422_AndNeverReRunsTheAction()
    {
        var store = new FakeIdempotencyStore();
        var filter = new IdempotencyActionFilter(store, Substitute.For<ICurrentUser>());
        var firstContext = CreateExecutingContext("key-1", new { Name = "A" });

        await filter.OnActionExecutionAsync(firstContext,
            () => Task.FromResult(CreateExecutedContext(firstContext, new ObjectResult(new { Id = 1 }) { StatusCode = 200 })));

        var secondContext = CreateExecutingContext("key-1", new { Name = "Different" });
        var secondCallRan = false;
        await filter.OnActionExecutionAsync(secondContext, () =>
        {
            secondCallRan = true;
            return Task.FromResult(CreateExecutedContext(secondContext, new OkObjectResult("should not run")));
        });

        Assert.False(secondCallRan);
        var result = Assert.IsType<ObjectResult>(secondContext.Result);
        Assert.Equal(422, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("https://api.aegisscribe.com/problems/idempotency-key-conflict", problem.Type);
    }

    [Fact]
    public async Task DifferentCallers_SameKey_DoNotCollide()
    {
        var store = new FakeIdempotencyStore();
        var userA = Substitute.For<ICurrentUser>();
        userA.Subject.Returns("user-a");
        var userB = Substitute.For<ICurrentUser>();
        userB.Subject.Returns("user-b");

        var contextA = CreateExecutingContext("key-1", new { Name = "A" });
        await new IdempotencyActionFilter(store, userA).OnActionExecutionAsync(contextA,
            () => Task.FromResult(CreateExecutedContext(contextA, new ObjectResult(new { Id = 1 }) { StatusCode = 200 })));

        var contextB = CreateExecutingContext("key-1", new { Name = "B" });
        var bCallRan = false;
        await new IdempotencyActionFilter(store, userB).OnActionExecutionAsync(contextB, () =>
        {
            bCallRan = true;
            return Task.FromResult(CreateExecutedContext(contextB, new ObjectResult(new { Id = 2 }) { StatusCode = 200 }));
        });

        Assert.True(bCallRan);
    }
}
