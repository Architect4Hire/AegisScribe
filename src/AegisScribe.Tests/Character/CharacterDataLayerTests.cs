using AegisScribe.Domain.Data;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using NSubstitute;

namespace AegisScribe.Tests.Character;

// DataLayer: a pass-through today (4.2, add-endpoint skill). These pin the delegation so the seam
// can't quietly start doing something else — the repository itself is exercised against real SQL
// by CharacterRepositoryTests.
public class CharacterDataLayerTests
{
    private readonly ICharacterRepository _repository = Substitute.For<ICharacterRepository>();
    private readonly CharacterDataLayer _dataLayer;
    private readonly Domain.Managers.Models.Domain.Character _character = new();

    public CharacterDataLayerTests()
    {
        _dataLayer = new CharacterDataLayer(_repository);
    }

    [Fact]
    public async Task FindByRealmAndName_Delegates()
    {
        _repository.FindByRealmAndNameAsync("us", "emberfall", "thrall", Arg.Any<CancellationToken>())
            .Returns(_character);

        var result = await _dataLayer.FindByRealmAndNameAsync("us", "emberfall", "thrall", CancellationToken.None);

        Assert.Same(_character, result);
    }

    [Fact]
    public async Task Search_Delegates()
    {
        var afterId = Guid.NewGuid();
        var summaries = new List<CharacterSummaryServiceModel> { new() };
        _repository.SearchAsync("us", "emberfall", "thra", "thrall", afterId, 25, Arg.Any<CancellationToken>())
            .Returns(summaries);

        var result = await _dataLayer.SearchAsync("us", "emberfall", "thra", "thrall", afterId, 25, CancellationToken.None);

        Assert.Same(summaries, result);
    }

    [Fact]
    public async Task ExecuteInTransaction_Delegates()
    {
        _repository.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(42);

        var result = await _dataLayer.ExecuteInTransactionAsync(_ => Task.FromResult(1), CancellationToken.None);

        Assert.Equal(42, result);
        await _repository.Received(1)
            .ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>());
    }
}
