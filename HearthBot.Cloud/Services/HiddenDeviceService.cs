using HearthBot.Cloud.Data;
using HearthBot.Cloud.Models;
using Microsoft.EntityFrameworkCore;

namespace HearthBot.Cloud.Services;

public class HiddenDeviceService
{
    private readonly CloudDbContext _db;

    public HiddenDeviceService(CloudDbContext db)
    {
        _db = db;
    }

    public async Task<HiddenDeviceEntry> HideAsync(string deviceId, string currentAccount, string orderNumber)
    {
        var existing = await _db.HiddenDeviceEntries.FirstOrDefaultAsync(entry =>
            entry.DeviceId == deviceId
            && entry.CurrentAccount == (currentAccount ?? string.Empty)
            && entry.OrderNumber == (orderNumber ?? string.Empty));

        if (existing != null)
            return existing;

        var hidden = new HiddenDeviceEntry
        {
            DeviceId = deviceId,
            CurrentAccount = currentAccount ?? string.Empty,
            OrderNumber = orderNumber ?? string.Empty,
            HiddenAt = DateTime.UtcNow
        };

        _db.HiddenDeviceEntries.Add(hidden);
        await _db.SaveChangesAsync();
        return hidden;
    }

    public async Task<bool> IsVisibleAsync(string deviceId, string currentAccount, string orderNumber)
    {
        var hidden = await _db.HiddenDeviceEntries.AnyAsync(entry =>
            entry.DeviceId == deviceId
            && entry.CurrentAccount == (currentAccount ?? string.Empty)
            && entry.OrderNumber == (orderNumber ?? string.Empty));

        return !hidden;
    }

    public async Task<List<Device>> FilterVisibleAsync(IEnumerable<Device> devices)
    {
        var deviceList = devices.ToList();
        if (deviceList.Count == 0) return deviceList;

        var deviceIds = deviceList.Select(device => device.DeviceId).Distinct().ToList();
        var hiddenEntries = await _db.HiddenDeviceEntries
            .Where(entry => deviceIds.Contains(entry.DeviceId))
            .ToListAsync();

        return deviceList
            .Where(device => !hiddenEntries.Any(entry =>
                entry.DeviceId == device.DeviceId
                && entry.CurrentAccount == (device.CurrentAccount ?? string.Empty)
                && entry.OrderNumber == (device.OrderNumber ?? string.Empty)))
            .ToList();
    }
}
