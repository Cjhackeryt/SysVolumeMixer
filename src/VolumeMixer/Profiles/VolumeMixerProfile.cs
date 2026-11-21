using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Profiles;
using Serilog;
using VolumeMixer.Mixer;

namespace VolumeMixer.Profiles;

internal static class VolumeMixerProfile
{
	public const string ProfileId = "volume-mixer";

	private const string FirstFolderId = "mixer";
	private const string FolderPrefix = "mixer-page-";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public static IReadOnlyList<VirtualProfileDescriptor> Build(VolumeMixerService mixer, ILogger logger)
	{
		var pageCount = mixer.GetPageCount();
		var folders = new List<VirtualFolderDescriptor>(pageCount);

		for (var page = 0; page < pageCount; page++)
		{
			var widgets = new List<VirtualWidgetDescriptor>(VolumeMixerService.SlotCount);
			var slots = mixer.GetSlotsForPage(page);
			for (var column = 0; column < VolumeMixerService.SlotCount; column++)
			{
				AddSlider(widgets, logger, page, column, slots[column]);
			}

			var folderId = page == 0 ? FirstFolderId : FolderPrefix + (page + 1).ToString(CultureInfo.InvariantCulture);
			var folderName = page == 0 ? "Mixer" : "Mixer " + (page + 1).ToString(CultureInfo.InvariantCulture);
			folders.Add(new VirtualFolderDescriptor(folderId, folderName, widgets, Order: page));
		}

		return
		[
			new VirtualProfileDescriptor(
				ProfileId,
				"SysVolumeMixer",
				ProfileLayout.Grid(rows: 3, columns: VolumeMixerService.SlotCount),
				folders),
		];
	}

	public static bool TryParseWidget(string folderId, string widgetId, out int page, out int column, out int direction)
	{
		page = 0;
		column = -1;
		direction = 0;

		if (!string.Equals(folderId, FirstFolderId, StringComparison.Ordinal))
		{
			if (!folderId.StartsWith(FolderPrefix, StringComparison.Ordinal) ||
				!int.TryParse(
					folderId[FolderPrefix.Length..],
					NumberStyles.None,
					CultureInfo.InvariantCulture,
					out var oneBasedPage) ||
				oneBasedPage < 2)
			{
				return false;
			}

			page = oneBasedPage - 1;
		}

		var separator = widgetId.IndexOf('-');
		if (separator < 0 ||
			!int.TryParse(
				widgetId[(separator + 1)..],
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out column) ||
			column is < 0 or >= VolumeMixerService.SlotCount)
		{
			return false;
		}

		if (widgetId.StartsWith("slider-", StringComparison.Ordinal) ||
			widgetId.StartsWith("app-", StringComparison.Ordinal))
		{
			return true;
		}

		if (widgetId.StartsWith("up-", StringComparison.Ordinal))
		{
			direction = 1;
			return true;
		}

		if (widgetId.StartsWith("down-", StringComparison.Ordinal))
		{
			direction = -1;
			return true;
		}

		return false;
	}

	private static void AddSlider(
		List<VirtualWidgetDescriptor> widgets,
		ILogger logger,
		int page,
		int column,
		Audio.AudioApplication? application)
	{
		var active = application is { IsActive: true };
		var id = $"slider-{column.ToString(CultureInfo.InvariantCulture)}";
		var widgetData = JsonSerializer.Serialize(new
		{
			label = active ? application!.DisplayName : string.Empty,
			showLabel = active,
			showValue = active,
			orientation = "vertical",
			min = 0,
			max = 100,
			step = 1,
			valueVariable = active
				? VolumeMixerSliderBinding.GetName(page, column)
				: "mixer_slider_empty_volume",
		}, JsonOptions);

		widgets.Add(new VirtualWidgetDescriptor(id, "Slider", column, 0, Width: 1, Height: 3, Data: widgetData));
		logger.Debug(
			"[SysVolumeMixer] Creating vertical slider: Page={Page} Column={Column} Id={WidgetId} Active={Active}",
			page + 1,
			column,
			id,
			active);
	}
}
