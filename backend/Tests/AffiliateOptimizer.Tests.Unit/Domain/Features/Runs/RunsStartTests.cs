using AffiliateOptimizer.Domain.Common;
using AffiliateOptimizer.Domain.Common.AffiliateService;
using AffiliateOptimizer.Domain.Common.Optimization;
using AffiliateOptimizer.Domain.Common.Optimization.Models;
using AffiliateOptimizer.Domain.Features.Runs.Start;
using AffiliateOptimizer.Tests.Unit.Support;
using Common.Domain.Exceptions;
using Common.Domain.Time;
using Moq;

namespace AffiliateOptimizer.Tests.Unit.Domain.Features.Runs;

public sealed class RunsStartTests
{
    private static readonly IReadOnlyList<Merchant> Merchants = [new(1, "Alpha"), new(2, "Beta")];
    private static readonly IOptimizationModelCatalog Catalog = new OptimizationModelCatalog([new RandomModel(), new EpsilonGreedyModel(new EpsilonGreedyOptions())]);

    private readonly Mock<RunsStart.IRepository> _repository = new();
    private readonly Mock<IAffiliateServiceClient> _client = new();
    private readonly Mock<ICredentialContext> _credentials = new();
    private readonly Mock<ISystemTime> _time = new();

    public RunsStartTests()
    {
        _repository.Setup(r => r.HasActiveRun(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repository.Setup(r => r.CreateRun(It.IsAny<RunsStart.IRepository.RunWriteModel>(), It.IsAny<IReadOnlyList<Network>>(), It.IsAny<IReadOnlyList<Merchant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        _client.Setup(c => c.GetMerchants(It.IsAny<CancellationToken>())).ReturnsAsync(Merchants);
        _client.Setup(c => c.GetNetworks(It.IsAny<CancellationToken>())).ReturnsAsync(Fixtures.Networks);
        _credentials.Setup(c => c.Fingerprint).Returns("fp-0001");
        _time.Setup(t => t.Now()).Returns(Fixtures.T0);
    }

    private RunsStart.CommandHandler Handler() => new(_repository.Object, _client.Object, Catalog, _credentials.Object, new EvaluationOptions(), _time.Object);

    [Fact]
    public async Task Handle_FetchesBothCataloguesOnceAndAnchorsTheRun()
    {
        RunsStart.IRepository.RunWriteModel? written = null;
        _repository.Setup(r => r.CreateRun(It.IsAny<RunsStart.IRepository.RunWriteModel>(), Fixtures.Networks, Merchants, It.IsAny<CancellationToken>()))
            .Callback<RunsStart.IRepository.RunWriteModel, IReadOnlyList<Network>, IReadOnlyList<Merchant>, CancellationToken>((model, _, _, _) => written = model)
            .ReturnsAsync(42);

        var result = await Handler().Handle(new RunsStart.Command(["random"], Interleaved: false, EvidenceWindowMinutes: null), CancellationToken.None);

        Assert.Equal(42, result.RunId);
        _client.Verify(c => c.GetMerchants(It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.GetNetworks(It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(written);
        Assert.Equal(["random"], written.Assignment.Names);
        Assert.False(written.Assignment.Interleaved);
        Assert.Equal(10, written.EvidenceWindowMinutes);
        Assert.Equal("fp-0001", written.CredentialFingerprint);
        Assert.Equal(Fixtures.T0, written.StartedAt);
        Assert.Contains("\"random\":{}", written.ParametersJson);
    }

    [Fact]
    public async Task Handle_InterleavedWithSeveralModels_RecordsParametersOfEach()
    {
        RunsStart.IRepository.RunWriteModel? written = null;
        _repository.Setup(r => r.CreateRun(It.IsAny<RunsStart.IRepository.RunWriteModel>(), It.IsAny<IReadOnlyList<Network>>(), It.IsAny<IReadOnlyList<Merchant>>(), It.IsAny<CancellationToken>()))
            .Callback<RunsStart.IRepository.RunWriteModel, IReadOnlyList<Network>, IReadOnlyList<Merchant>, CancellationToken>((model, _, _, _) => written = model)
            .ReturnsAsync(7);

        await Handler().Handle(new RunsStart.Command(["random", "epsilon-greedy"], Interleaved: true, EvidenceWindowMinutes: 20), CancellationToken.None);

        Assert.NotNull(written);
        Assert.True(written.Assignment.Interleaved);
        Assert.Equal(20, written.EvidenceWindowMinutes);
        Assert.Contains("\"epsilon-greedy\":{\"epsilon\":0.1", written.ParametersJson);
    }

    [Fact]
    public async Task Handle_InterleavedFlagWithOneModel_IsNotInterleaved()
    {
        RunsStart.IRepository.RunWriteModel? written = null;
        _repository.Setup(r => r.CreateRun(It.IsAny<RunsStart.IRepository.RunWriteModel>(), It.IsAny<IReadOnlyList<Network>>(), It.IsAny<IReadOnlyList<Merchant>>(), It.IsAny<CancellationToken>()))
            .Callback<RunsStart.IRepository.RunWriteModel, IReadOnlyList<Network>, IReadOnlyList<Merchant>, CancellationToken>((model, _, _, _) => written = model)
            .ReturnsAsync(7);

        await Handler().Handle(new RunsStart.Command(["random"], Interleaved: true, null), CancellationToken.None);

        Assert.False(written!.Assignment.Interleaved);
    }

    [Fact]
    public async Task Handle_ActiveRunExists_RefusesWithoutTouchingTheServer()
    {
        _repository.Setup(r => r.HasActiveRun(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<LogicErrorException>(() => Handler().Handle(new RunsStart.Command(["random"], false, null), CancellationToken.None));

        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_CatalogueFetchFails_PropagatesRatherThanRoutingBlind()
    {
        _client.Setup(c => c.GetNetworks(It.IsAny<CancellationToken>())).ThrowsAsync(new AffiliateServiceException(401, "UNAUTHORIZED", "bad credentials"));

        var error = await Assert.ThrowsAsync<AffiliateServiceException>(() => Handler().Handle(new RunsStart.Command(["random"], false, null), CancellationToken.None));

        Assert.Equal("UNAUTHORIZED", error.Code);
        _repository.Verify(r => r.CreateRun(It.IsAny<RunsStart.IRepository.RunWriteModel>(), It.IsAny<IReadOnlyList<Network>>(), It.IsAny<IReadOnlyList<Merchant>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public sealed class RunsStartCommandValidatorTests
{
    private static readonly RunsStart.CommandValidator Validator = new(new OptimizationModelCatalog([new RandomModel(), new AdvertisedGreedyModel()]));

    [Fact]
    public async Task Validate_ValidCommand_NoErrors()
        => Assert.Empty(await Validator.Validate(new RunsStart.Command(["random", "advertised-greedy"], true, 15)));

    [Fact]
    public async Task Validate_NoModels_IsAnError()
        => Assert.Contains(await Validator.Validate(new RunsStart.Command([], false, null)), e => e.FieldName == "modelNames");

    [Fact]
    public async Task Validate_UnknownModel_NamesTheKnownOnes()
    {
        var errors = await Validator.Validate(new RunsStart.Command(["nope"], false, null));

        var error = Assert.Single(errors);
        Assert.Contains("advertised-greedy", error.Message);
        Assert.Equal("modelNames[0]", error.FieldName);
    }

    [Fact]
    public async Task Validate_DuplicateModel_IsAnError()
        => Assert.Contains(await Validator.Validate(new RunsStart.Command(["random", "random"], true, null)), e => e.Message.Contains("once"));

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public async Task Validate_WindowOutOfRange_IsAnError(int minutes)
        => Assert.Contains(await Validator.Validate(new RunsStart.Command(["random"], false, minutes)), e => e.FieldName == "evidenceWindowMinutes");
}
