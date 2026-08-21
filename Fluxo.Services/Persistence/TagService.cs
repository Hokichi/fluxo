using AutoMapper;
using Fluxo.Core.DTO;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Services.Persistence;

public sealed class TagService(IAppDataService appData, IMapper mapper) : ITagService
{
    public async Task<IReadOnlyList<TagDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var tags = await appData.GetTagsAsync(cancellationToken).ConfigureAwait(false);
        return mapper.Map<IReadOnlyList<TagDto>>(tags);
    }

    public async Task AddAsync(TagDto dto, CancellationToken cancellationToken = default)
    {
        var tag = mapper.Map<Tag>(dto);
        tag.Id = 0;
        await appData.AddTagAsync(tag, cancellationToken).ConfigureAwait(false);
        await appData.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(TagDto dto, CancellationToken cancellationToken = default)
    {
        var tag = await appData.GetTagByIdAsync(dto.Id, cancellationToken).ConfigureAwait(false);
        if (tag is null)
            return;

        mapper.Map(dto, tag);
        appData.UpdateTag(tag);
        await appData.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        var tag = await appData.GetTagByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (tag is null)
            return;

        appData.RemoveTag(tag);
        await appData.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
