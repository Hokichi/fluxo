using Fluxo.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fluxo.Data.Context;

public sealed class FluxoDbContext(DbContextOptions<FluxoDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<SavingGoal> SavingGoals => Set<SavingGoal>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<BudgetAllocation> BudgetAllocation => Set<BudgetAllocation>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareTransactions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareTransactions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTransaction(modelBuilder.Entity<Transaction>());
        ConfigureTag(modelBuilder.Entity<Tag>());
        ConfigureSavingGoal(modelBuilder.Entity<SavingGoal>());
        ConfigureAccount(modelBuilder.Entity<Account>());
        ConfigureRecurringTransaction(modelBuilder.Entity<RecurringTransaction>());
        ConfigureUserSettings(modelBuilder.Entity<UserSettings>());
        ConfigureBudgetAllocation(modelBuilder.Entity<BudgetAllocation>());
        ConfigureReferenceAutoIncludes(modelBuilder);
    }

    private static void ConfigureTag(EntityTypeBuilder<Tag> entity)
    {
        entity.ToTable("Tags");
        entity.Property(tag => tag.Id).ValueGeneratedOnAdd();
        entity.HasKey(tag => tag.Id);

        entity.Property(tag => tag.Name).IsRequired();
        entity.Property(tag => tag.HexCode).IsRequired();
        entity.Property(tag => tag.IsSystemTag).HasDefaultValue(false);
        entity.Property(tag => tag.SpendingLimit).HasColumnType("NUMERIC");
    }

    private static void ConfigureSavingGoal(EntityTypeBuilder<SavingGoal> entity)
    {
        entity.ToTable("SavingGoals");
        entity.Property(goal => goal.Id).ValueGeneratedOnAdd();
        entity.HasKey(goal => goal.Id);

        entity.Property(goal => goal.Name).IsRequired();
        entity.Property(goal => goal.TargetAmount).HasColumnType("NUMERIC");
        entity.Property(goal => goal.CurrentAmount).HasColumnType("NUMERIC");
        entity.Property(goal => goal.SavingEndDate);
        entity.Property(goal => goal.CreatedOn);
    }

    private static void ConfigureAccount(EntityTypeBuilder<Account> entity)
    {
        entity.ToTable("Accounts");
        entity.Property(source => source.Id).ValueGeneratedOnAdd();
        entity.HasKey(source => source.Id);

        entity.Property(source => source.Name).IsRequired();
        entity.Property(source => source.AccountLimit).HasColumnType("NUMERIC");
        entity.Property(source => source.MaximumSpending).HasColumnType("NUMERIC");
        entity.Property(source => source.MinimumPayment).HasColumnType("NUMERIC");
        entity.Property(source => source.SpentAmount).HasColumnType("NUMERIC");
        entity.Property(source => source.Balance).HasColumnType("NUMERIC");
        entity.Property(source => source.MonthlyDueDate);
        entity.Property(source => source.DeductSource);
        entity.Property(source => source.IsEnabled);
        entity.Property(source => source.IsDefault).HasDefaultValue(false);
        entity.HasIndex(source => source.IsDefault)
            .IsUnique()
            .HasFilter("IsDefault = 1");
        entity.Property(source => source.PinnedOnUI);
        entity.Property(source => source.InterestRate).HasColumnType("REAL");
    }

    private static void ConfigureRecurringTransaction(EntityTypeBuilder<RecurringTransaction> entity)
    {
        entity.ToTable("RecurringTransactions");
        entity.Property(transaction => transaction.Id).ValueGeneratedOnAdd();
        entity.HasKey(transaction => transaction.Id);

        entity.Property(transaction => transaction.Name).IsRequired();
        entity.Property(transaction => transaction.Amount).HasColumnType("NUMERIC");
        entity.Property(transaction => transaction.RecurringPeriod);
        entity.Property(transaction => transaction.RecurringTime);
        entity.Property(transaction => transaction.Type);
        entity.Property(transaction => transaction.Category);
        entity.Property(transaction => transaction.IsEnabled);
        entity.Property(transaction => transaction.EndDate);

        entity.HasOne(transaction => transaction.Source)
            .WithMany()
            .HasForeignKey(transaction => transaction.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(transaction => transaction.Tag)
            .WithMany()
            .HasForeignKey(transaction => transaction.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(transaction => transaction.Goal)
            .WithMany()
            .HasForeignKey(transaction => transaction.GoalId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureUserSettings(EntityTypeBuilder<UserSettings> entity)
    {
        entity.ToTable("UserSettings");
        entity.HasKey(settings => settings.Name);

        entity.Property(settings => settings.Name).IsRequired();
        entity.Property(settings => settings.Value).IsRequired();
    }

    private static void ConfigureTransaction(EntityTypeBuilder<Transaction> entity)
    {
        entity.ToTable("Transactions", table =>
        {
            table.HasCheckConstraint("CK_Transactions_Amount", "Amount >= 0");
            table.HasCheckConstraint(
                "CK_Transactions_ShouldAffectBalance_RequiresIoU",
                "ShouldAffectBalance = 0 OR IsIoU = 1");
        });
        entity.HasKey(transaction => transaction.Id);
        entity.Ignore(transaction => transaction.AffectsAccountBalance);
        entity.Property(transaction => transaction.Id).ValueGeneratedOnAdd();
        entity.Property(transaction => transaction.Name).IsRequired();
        entity.Property(transaction => transaction.Amount).HasColumnType("NUMERIC");
        entity.Property(transaction => transaction.Notes).IsRequired();
        entity.Property(transaction => transaction.LoggedOn);
        entity.Property(transaction => transaction.IsPinned).HasDefaultValue(false);
        entity.Property(transaction => transaction.IsForDeletion).HasDefaultValue(false);
        entity.Property(transaction => transaction.IsIoU).HasDefaultValue(false);
        entity.Property(transaction => transaction.ShouldAffectBalance).HasDefaultValue(false);
        entity.HasOne(transaction => transaction.Account).WithMany().HasForeignKey(transaction => transaction.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(transaction => transaction.Goal).WithMany().HasForeignKey(transaction => transaction.GoalId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(transaction => transaction.RepaymentAccount).WithMany().HasForeignKey(transaction => transaction.RepaymentAccountId).OnDelete(DeleteBehavior.SetNull);
        entity.HasOne(transaction => transaction.RelatedRecurringTransaction).WithMany().HasForeignKey(transaction => transaction.RelatedRecurringTransactionId).OnDelete(DeleteBehavior.SetNull);
        entity.HasIndex(transaction => transaction.RelatedRecurringTransactionId);
        entity.HasOne(transaction => transaction.Tag).WithMany().HasForeignKey(transaction => transaction.TagId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(transaction => transaction.ParentTransaction).WithMany().HasForeignKey(transaction => transaction.ParentTransactionId).OnDelete(DeleteBehavior.Restrict);
    }

    private void PrepareTransactions()
    {
        foreach (var entry in ChangeTracker.Entries<Transaction>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            if (!entry.Entity.IsIoU)
                entry.Entity.ShouldAffectBalance = false;

            entry.Entity.OccurredOn = entry.Entity.OccurredOn.Date;
            if (entry.State == EntityState.Added)
                entry.Entity.LoggedOn = DateTime.Now;
        }
    }

    private static void ConfigureBudgetAllocation(EntityTypeBuilder<BudgetAllocation> entity)
    {
        entity.ToTable("BudgetAllocation");
        entity.Property(allocation => allocation.Id).ValueGeneratedOnAdd();
        entity.HasKey(allocation => allocation.Id);

        entity.Property(allocation => allocation.NeedsThreshold).IsRequired();
        entity.Property(allocation => allocation.WantsThreshold).IsRequired();
        entity.Property(allocation => allocation.InvestThreshold).IsRequired();
        entity.Property(allocation => allocation.AllocationPeriod).IsRequired();
        entity.Property(allocation => allocation.PeriodStart).IsRequired();
        entity.Property(allocation => allocation.CurrentPeriodIndex).IsRequired();
        entity.Property(allocation => allocation.LastRolloverPeriodStart).IsRequired();
        entity.Property(allocation => allocation.AllocationLimit).HasColumnType("NUMERIC");
        entity.Property(allocation => allocation.NeedsDebt).HasColumnType("NUMERIC");
        entity.Property(allocation => allocation.WantsDebt).HasColumnType("NUMERIC");
        entity.Property(allocation => allocation.InvestDebt).HasColumnType("NUMERIC");
        entity.Property(allocation => allocation.RolloverPolicy).IsRequired();
        entity.Property(allocation => allocation.OverspendPolicy).IsRequired();

        entity.Property<int>("SingletonKey")
            .HasDefaultValue(1)
            .IsRequired();
        entity.HasIndex("SingletonKey").IsUnique();
    }

    private static void ConfigureReferenceAutoIncludes(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType is null)
                continue;

            foreach (var navigation in entityType.GetNavigations())
            {
                if (navigation.IsCollection)
                    continue;

                var isOptionalSelfReference =
                    navigation.DeclaringEntityType.ClrType == navigation.TargetEntityType.ClrType &&
                    !navigation.ForeignKey.IsRequired;

                if (isOptionalSelfReference)
                    continue;

                modelBuilder.Entity(entityType.ClrType)
                    .Navigation(navigation.Name)
                    .AutoInclude();
            }
        }
    }
}
