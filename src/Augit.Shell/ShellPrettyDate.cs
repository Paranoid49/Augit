using System.Globalization;

namespace Augit.Shell;

/// <summary>文件历史沿用DateFormatUtil的近期日期边界；显示文字与完整日期时间分开。</summary>
internal static class ShellPrettyDate
{
    internal static string Format(DateTimeOffset value, DateTimeOffset now)
    {
        TimeSpan age = now - value;
        if (age >= TimeSpan.Zero && age <= TimeSpan.FromMinutes(61))
        {
            int minutes = (int)Math.Round(age.TotalMinutes, MidpointRounding.ToEven);
            return minutes == 0 ? "Moments ago" : minutes == 1 ? "A minute ago" : minutes >= 60 ? "1 hour ago" : $"{minutes} minutes ago";
        }

        DateTime local = value.ToLocalTime().DateTime;
        DateTime today = now.ToLocalTime().Date;
        string time = local.ToString("H:mm", CultureInfo.InvariantCulture);
        if (local.Date == today)
        {
            return $"Today {time}";
        }

        if (local.Date == today.AddDays(-1))
        {
            return $"Yesterday {time}";
        }

        return local.ToString("yyyy/M/d H:mm", CultureInfo.InvariantCulture);
    }
}
