using HearthBot.Cloud.Models;

namespace HearthBot.Cloud.Services;

public sealed record OrderCompletionUpdate(Device Device, bool WasNewlyCompleted);
