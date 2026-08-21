using AutoMapper;
using Fluxo.Core.DTO;
using Fluxo.Core.Entities;
using Fluxo.Core.Filters;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Services.Persistence;

public sealed class AccountService(IAppDataService appData, IMapper mapper) : IAccountService
{
    public async Task<IReadOnlyList<AccountDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var sources = await appData.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
        return mapper.Map<IReadOnlyList<AccountDto>>(sources);
    }

    public async Task<AccountDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var source = await appData.GetAccountByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return source is null ? null : mapper.Map<AccountDto>(source);
    }

    public async Task<IReadOnlyList<AccountDto>> SearchAsync(AccountFilter filter,
        CancellationToken cancellationToken = default)
    {
        var sources = await appData.SearchAccountsAsync(filter, cancellationToken).ConfigureAwait(false);
        return mapper.Map<IReadOnlyList<AccountDto>>(sources);
    }

    public async Task AddAsync(AccountDto dto, CancellationToken cancellationToken = default)
    {
        var source = mapper.Map<Account>(dto);
        source.Id = 0;
        await appData.AddAccountAsync(source, cancellationToken).ConfigureAwait(false);
        await appData.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
