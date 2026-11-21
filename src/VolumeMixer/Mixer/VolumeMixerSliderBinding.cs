using System.Globalization;

namespace VolumeMixer.Mixer;

internal static class VolumeMixerSliderBinding
{
	private const string IdPrefix = "slider-page-";
	private const string IdSeparator = "-slot-";
	private const string IdSuffix = "-volume";
	private const string NamePrefix = "mixer_slider_page_";
	private const string NameSeparator = "_slot_";
	private const string NameSuffix = "_volume";

	public static string GetId(int page, int slot) =>
		$"slider-page-{(page + 1).ToString(CultureInfo.InvariantCulture)}-slot-{(slot + 1).ToString(CultureInfo.InvariantCulture)}-volume";

	public static string GetName(int page, int slot) =>
		$"mixer_slider_page_{(page + 1).ToString(CultureInfo.InvariantCulture)}_slot_{(slot + 1).ToString(CultureInfo.InvariantCulture)}_volume";

	public static bool TryParse(string identifier, out int page, out int slot)
	{
		if (TryParse(identifier.AsSpan(), IdPrefix, IdSeparator, IdSuffix, out page, out slot) ||
			TryParse(identifier.AsSpan(), NamePrefix, NameSeparator, NameSuffix, out page, out slot))
		{
			page--;
			slot--;
			return slot < VolumeMixerService.SlotCount;
		}

		page = -1;
		slot = -1;
		return false;
	}

	private static bool TryParse(
		ReadOnlySpan<char> identifier,
		string prefix,
		string separator,
		string suffix,
		out int page,
		out int slot)
	{
		page = 0;
		slot = 0;
		if (!identifier.StartsWith(prefix, StringComparison.Ordinal))
		{
			return false;
		}

		var values = identifier[prefix.Length..];
		var separatorIndex = values.IndexOf(separator, StringComparison.Ordinal);
		var suffixIndex = values.LastIndexOf(suffix, StringComparison.Ordinal);
		if (separatorIndex <= 0 ||
			suffixIndex <= separatorIndex + separator.Length ||
			suffixIndex + suffix.Length != values.Length ||
			!int.TryParse(values[..separatorIndex], NumberStyles.None, CultureInfo.InvariantCulture, out page) ||
			!int.TryParse(values[(separatorIndex + separator.Length)..suffixIndex], NumberStyles.None, CultureInfo.InvariantCulture, out slot) ||
			page < 1 ||
			slot is < 1 or > VolumeMixerService.SlotCount)
		{
			return false;
		}

		return true;
	}
}
