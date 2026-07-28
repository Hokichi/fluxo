using AutoMapper;
using Fluxo.Core.DTO;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Mappings;

public sealed class DtoViewModelProfile : Profile
{
    public DtoViewModelProfile()
    {
        CreateMap<TransactionDto, TransactionVM>().ReverseMap();
        CreateMap<TagDto, TagVM>().ReverseMap();
        CreateMap<SavingGoalDto, SavingGoalVM>().ReverseMap();
        CreateMap<AccountDto, AccountVM>().ReverseMap();
        CreateMap<RecurringTransactionDto, RecurringTransactionVM>().ReverseMap();
    }
}
