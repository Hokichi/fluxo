using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Fluxo.DataModels.Messages;

public sealed class DashboardDailyDateRequestedMessage : RequestMessage<DateTime?>;
