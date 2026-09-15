using AegisScribe.Domain.Business;
using AegisScribe.Domain.Facade;
using AegisScribe.Domain.Managers.Models.ServiceModels;
using AegisScribe.Domain.Managers.Models.ViewModels;
using AegisScribe.Domain.Managers.Validators;
using FluentValidation;
using NSubstitute;

namespace AegisScribe.Tests.Tenancy;

// Facade: real validators, mocked business. Nothing cached yet (2.8 hasn't landed), so this is purely
// "validate, then delegate" — matching AuthFacadeTests' style.
public class TenantFacadeTests
{
    private readonly ITenantBusiness _business = Substitute.For<ITenantBusiness>();
    private readonly TenantFacade _facade;

    public TenantFacadeTests()
    {
        _facade = new TenantFacade(
            _business,
            new CreateTenantViewModelValidator(),
            new RenameTenantViewModelValidator(),
            new SlugCheckViewModelValidator());
    }

    [Fact]
    public async Task CheckSlug_Valid_Delegates()
    {
        var viewModel = new SlugCheckViewModel { Name = "Ashes of Dawn" };
        var expected = new SlugCheckServiceModel { Slug = "ashes-of-dawn", Available = true, Reason = SlugCheckReason.Available };
        _business.CheckSlugAsync(viewModel, Arg.Any<CancellationToken>()).Returns(expected);

        Assert.Same(expected, await _facade.CheckSlugAsync(viewModel, CancellationToken.None));
    }

    [Theory]
    [InlineData(null, null)]                      // neither
    [InlineData("Ashes of Dawn", "ashes")]        // both
    public async Task CheckSlug_NotExactlyOneInput_ThrowsValidationException(string? name, string? slug)
    {
        var viewModel = new SlugCheckViewModel { Name = name, Slug = slug };

        await Assert.ThrowsAsync<ValidationException>(() => _facade.CheckSlugAsync(viewModel, CancellationToken.None));
        await _business.DidNotReceiveWithAnyArgs().CheckSlugAsync(default!, default);
    }

    [Fact]
    public async Task CheckSlug_MalformedSlug_IsNotAValidationFailure()
    {
        // The form asks on every keystroke, so a half-typed slug must come back as an ANSWER
        // (Reason = Invalid) rather than a 400 the field cannot render.
        var viewModel = new SlugCheckViewModel { Slug = "Not A Slug!" };
        var expected = new SlugCheckServiceModel { Slug = "Not A Slug!", Available = false, Reason = SlugCheckReason.Invalid };
        _business.CheckSlugAsync(viewModel, Arg.Any<CancellationToken>()).Returns(expected);

        Assert.Same(expected, await _facade.CheckSlugAsync(viewModel, CancellationToken.None));
    }

    [Fact]
    public async Task Create_NoSlug_IsValid_WhenTheNameCanBeDerivedFrom()
    {
        var viewModel = new CreateTenantViewModel { Slug = null, Name = "Ashes of Dawn", TimeZoneId = "UTC" };
        var expected = new TenantServiceModel { Id = Guid.NewGuid(), Slug = "ashes-of-dawn", Name = "Ashes of Dawn", TimeZoneId = "UTC" };
        _business.CreateAsync(viewModel, Arg.Any<CancellationToken>()).Returns(expected);

        Assert.Same(expected, await _facade.CreateAsync(viewModel, CancellationToken.None));
    }

    [Fact]
    public async Task Create_NoSlug_AndANameThatFoldsToNothing_ThrowsValidationException()
    {
        var viewModel = new CreateTenantViewModel { Slug = null, Name = "!!!", TimeZoneId = "UTC" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _facade.CreateAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateTenantViewModel.Slug));
        await _business.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Create_ReservedSlug_ThrowsValidationException()
    {
        var viewModel = new CreateTenantViewModel { Slug = "admin", Name = "Admin", TimeZoneId = "UTC" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _facade.CreateAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateTenantViewModel.Slug));
        await _business.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Create_UnrecognizedTimeZone_ThrowsValidationException()
    {
        var viewModel = new CreateTenantViewModel { Slug = "emberwatch", Name = "Emberwatch", TimeZoneId = "Middle/Earth" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _facade.CreateAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateTenantViewModel.TimeZoneId));
        await _business.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task Create_Valid_DelegatesAndReturnsTheServiceModel()
    {
        var viewModel = new CreateTenantViewModel { Slug = "emberwatch", Name = "Emberwatch", TimeZoneId = "UTC" };
        var expected = new TenantServiceModel { Id = Guid.NewGuid(), Slug = "emberwatch", Name = "Emberwatch", TimeZoneId = "UTC" };
        _business.CreateAsync(viewModel, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _facade.CreateAsync(viewModel, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task Create_Invalid_ThrowsValidationException_AndNeverReachesBusiness()
    {
        var viewModel = new CreateTenantViewModel { Slug = "Not A Slug!", Name = "", TimeZoneId = "not-a-timezone" };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _facade.CreateAsync(viewModel, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateTenantViewModel.Slug));
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateTenantViewModel.Name));
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateTenantViewModel.TimeZoneId));
        await _business.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task GetById_Delegates()
    {
        var tenantId = Guid.NewGuid();
        var expected = new TenantServiceModel { Id = tenantId, Slug = "emberwatch", Name = "Emberwatch", TimeZoneId = "UTC" };
        _business.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(expected);

        Assert.Same(expected, await _facade.GetByIdAsync(tenantId, CancellationToken.None));
    }

    [Fact]
    public async Task Rename_Valid_Delegates()
    {
        var tenantId = Guid.NewGuid();
        var viewModel = new RenameTenantViewModel { Name = "Renamed" };
        var expected = new TenantServiceModel { Id = tenantId, Slug = "emberwatch", Name = "Renamed", TimeZoneId = "UTC" };
        _business.RenameAsync(tenantId, viewModel, Arg.Any<CancellationToken>()).Returns(expected);

        Assert.Same(expected, await _facade.RenameAsync(tenantId, viewModel, CancellationToken.None));
    }

    [Fact]
    public async Task Rename_Invalid_ThrowsValidationException_AndNeverReachesBusiness()
    {
        var viewModel = new RenameTenantViewModel { Name = "" };

        await Assert.ThrowsAsync<ValidationException>(() => _facade.RenameAsync(Guid.NewGuid(), viewModel, CancellationToken.None));

        await _business.DidNotReceiveWithAnyArgs().RenameAsync(default, default!, default);
    }
}
