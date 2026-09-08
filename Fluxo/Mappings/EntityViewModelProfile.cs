using AutoMapper;
using Fluxo.Core.Entities;
using Fluxo.ViewModels.Entities;

namespace Fluxo.Mappings;

public sealed class EntityViewModelProfile : Profile
{
    public EntityViewModelProfile()
    {
        CreateMap<Account, AccountVM>(MemberList.None)
            .ForMember(viewModel => viewModel.MoneyIn, options => options.Ignore())
            .ForMember(viewModel => viewModel.MoneyOut, options => options.Ignore());
        CreateMap<Tag, TagVM>(MemberList.None);
        CreateMap<Transaction, TransactionVM>(MemberList.None);
        CreateMap<SavingGoal, SavingGoalVM>(MemberList.None);
        CreateMap<RecurringTransaction, RecurringTransactionVM>(MemberList.None);
    }
}
