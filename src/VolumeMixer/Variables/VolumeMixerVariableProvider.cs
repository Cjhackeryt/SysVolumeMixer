using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;
using Serilog;
using VolumeMixer.Mixer;

namespace VolumeMixer.Variables;

public sealed class VolumeMixerVariableProvider
{
	private readonly VolumeMixerService _mixer;
	private readonly ILogger _logger;
	private readonly List<VariableDefinition> _eagerVariables;

	public VolumeMixerVariableProvider(VolumeMixerService mixer, ILogger logger)
	{
		_mixer = mixer;
		_logger = logger.ForContext<VolumeMixerVariableProvider>();
		_eagerVariables = BuildDefinitions();
	}

	public IReadOnlyList<VariableDefinition> Variables =>
		_eagerVariables.Concat(BuildSliderDefinitions()).ToArray();

	public IReadOnlyList<VariableDefinition> DeclaredVariables => Variables;

	public static bool VariablesDependOnConfiguration => false;

	public static bool SupportsCatalog => false;

	public static bool SupportsPush => false;

	public static bool SupportsSearch => false;

	public static string CatalogName => "VolumeMixer";

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (string.Equals(localId, "slider-empty-volume", StringComparison.Ordinal))
		{
			return ValueTask.FromResult(VariableReading.Of(0.0, 0, 100, 1));
		}

		if (VolumeMixerSliderBinding.TryParse(localId, out var page, out var sliderSlot))
		{
			if (page >= _mixer.GetPageCount())
			{
				return ValueTask.FromResult(VariableReading.Unavailable);
			}

			var sliderApplication = _mixer.GetSlotsForPage(page)[sliderSlot];
			return sliderApplication is null
				? ValueTask.FromResult(VariableReading.Unavailable)
				: ValueTask.FromResult(VariableReading.Of((double)sliderApplication.VolumePercent, 0, 100, 1));
		}

		if (!TryParseLocalId(localId, out var kind, out var slot))
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		if (kind == "page")
		{
			return ValueTask.FromResult(VariableReading.Of(_mixer.GetPage() + 1));
		}

		if (kind == "page-count")
		{
			return ValueTask.FromResult(VariableReading.Of(_mixer.GetPageCount()));
		}

		if (kind == "total")
		{
			return ValueTask.FromResult(VariableReading.Of(_mixer.GetTotalCount()));
		}

		var application = _mixer.GetSlotsForPage(_mixer.GetPage())[slot];
		if (application is null)
		{
			return kind switch
			{
				"app" => ValueTask.FromResult(VariableReading.Of("Empty")),
				"volume" => ValueTask.FromResult(VariableReading.Of(0.0, 0, 100, 1)),
				"muted" => ValueTask.FromResult(VariableReading.Of(false)),
				"active" => ValueTask.FromResult(VariableReading.Of(false)),
				_ => ValueTask.FromResult(VariableReading.Unavailable),
			};
		}

		return kind switch
		{
			"app" => ValueTask.FromResult(VariableReading.Of(
				application.DisplayName + " " + application.VolumePercent.ToString(CultureInfo.InvariantCulture) + "%")),
			"volume" => ValueTask.FromResult(VariableReading.Of((double)application.VolumePercent, 0, 100, 1)),
			"muted" => ValueTask.FromResult(VariableReading.Of(application.IsMuted)),
			"active" => ValueTask.FromResult(VariableReading.Of(application.IsActive)),
			_ => ValueTask.FromResult(VariableReading.Unavailable),
		};
	}

	public ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!VolumeMixerSliderBinding.TryParse(localId, out var page, out var slot))
		{
			return ValueTask.FromResult(VariableWriteResult.NotWritable(Strings.Variables.ReadOnly.Message()));
		}

		if (!TryParseVolume(value, out var volumePercent))
		{
			return ValueTask.FromResult(VariableWriteResult.InvalidValue(Strings.Variables.SliderVolume.InvalidValue()));
		}

		if (!_mixer.TrySetSlotVolume(page, slot, volumePercent, out var application, out _, out var error))
		{
			if (application is null)
			{
				_logger.Debug(
					"[SysVolumeMixer] Ignored slider write for an empty or unavailable column {Column} on page {Page}: {Reason}",
					slot,
					page + 1,
					error);
				return ValueTask.FromResult(VariableWriteResult.Unavailable(Strings.Actions.Errors.NoApplication()));
			}

			_logger.Warning(
				"[SysVolumeMixer] Slider volume change failed for {Application} on page {Page}: {Reason}",
				application.DisplayName,
				page + 1,
				error);
			return ValueTask.FromResult(VariableWriteResult.Failed(Strings.Actions.Errors.Unexpected()));
		}

		return ValueTask.FromResult(VariableWriteResult.Applied());
	}

	public ValueTask<VariableDefinition?> ResolveAsync(string localId, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (VolumeMixerSliderBinding.TryParse(localId, out var page, out var slot))
		{
			return ValueTask.FromResult<VariableDefinition?>(BuildSliderDefinition(page, slot));
		}

		return ValueTask.FromResult(Variables.FirstOrDefault(definition =>
			string.Equals(definition.Id, localId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(definition.ResolvedId, localId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(definition.Name, localId, StringComparison.OrdinalIgnoreCase)));
	}

	public static ValueTask<IReadOnlyList<VariableValue>> SubscribeAsync(
		IReadOnlyCollection<string> localIds,
		CancellationToken cancellationToken) =>
		ValueTask.FromResult<IReadOnlyList<VariableValue>>([]);

	public static Task OnAttachedAsync(IVariableSink sink, CancellationToken cancellationToken) => Task.CompletedTask;

	private static bool TryParseLocalId(string localId, out string kind, out int slot)
	{
		kind = string.Empty;
		slot = -1;
		if (string.Equals(localId, "page", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(localId, "page-count", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(localId, "total", StringComparison.OrdinalIgnoreCase))
		{
			kind = localId.ToLowerInvariant();
			return true;
		}

		if (!localId.StartsWith("slot-", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var separator = localId.IndexOf('-', 5);
		if (separator < 0 ||
			!int.TryParse(localId[5..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var oneBased) ||
			oneBased is < 1 or > VolumeMixerService.SlotCount)
		{
			return false;
		}

		slot = oneBased - 1;
		kind = localId[(separator + 1)..].ToLowerInvariant();
		return kind is "app" or "volume" or "muted" or "active";
	}

	private static List<VariableDefinition> BuildDefinitions()
	{
		var definitions = new List<VariableDefinition>();
		for (var index = 1; index <= VolumeMixerService.SlotCount; index++)
		{
			var slot = index.ToString(CultureInfo.InvariantCulture);
			definitions.Add(VariableDefinition.Eager($"mixer_slot{slot}_app", VariableType.Text) with
			{
				Id = $"slot-{slot}-app",
				DisplayName = Strings.Variables.SlotApp.Display(slot),
				Description = Strings.Variables.SlotApp.Description(),
			});
			definitions.Add(VariableDefinition.Eager($"mixer_slot{slot}_volume", VariableType.Numeric, 0, TimeSpan.FromSeconds(2)) with
			{
				Id = $"slot-{slot}-volume",
				DisplayName = Strings.Variables.SlotVolume.Display(slot),
				Description = Strings.Variables.SlotVolume.Description(),
				Unit = "%",
			});
			definitions.Add(VariableDefinition.Eager($"mixer_slot{slot}_muted", VariableType.Boolean) with
			{
				Id = $"slot-{slot}-muted",
				DisplayName = Strings.Variables.SlotMuted.Display(slot),
				Description = Strings.Variables.SlotMuted.Description(),
			});
			definitions.Add(VariableDefinition.Eager($"mixer_slot{slot}_active", VariableType.Boolean) with
			{
				Id = $"slot-{slot}-active",
				DisplayName = Strings.Variables.SlotActive.Display(slot),
				Description = Strings.Variables.SlotActive.Description(),
			});
		}

		definitions.Add(VariableDefinition.Eager("mixer_page", VariableType.Numeric) with
		{
			Id = "page",
			DisplayName = Strings.Variables.Page.Display(),
			Description = Strings.Variables.Page.Description(),
		});
		definitions.Add(VariableDefinition.Eager("mixer_page_count", VariableType.Numeric) with
		{
			Id = "page-count",
			DisplayName = Strings.Variables.PageCount.Display(),
			Description = Strings.Variables.PageCount.Description(),
		});
		definitions.Add(VariableDefinition.Eager("mixer_total_apps", VariableType.Numeric) with
		{
			Id = "total",
			DisplayName = Strings.Variables.Total.Display(),
			Description = Strings.Variables.Total.Description(),
		});
		definitions.Add(VariableDefinition.Eager(
			"mixer_slider_empty_volume",
			VariableType.Numeric,
			0,
			TimeSpan.FromSeconds(1)) with
		{
			Id = "slider-empty-volume",
			DisplayName = Strings.Variables.SliderEmpty.Display(),
			Description = Strings.Variables.SliderEmpty.Description(),
			Unit = "%",
		});
		return definitions;
	}

	private VariableDefinition BuildSliderDefinition(int page, int slot)
	{
		var application = page < _mixer.GetPageCount()
			? _mixer.GetSlotsForPage(page)[slot]
			: null;
		return BuildSliderDefinition(page, slot, application);
	}

	private static VariableDefinition BuildSliderDefinition(int page, int slot, Audio.AudioApplication? application)
	{
		return VariableDefinition.Eager(
			VolumeMixerSliderBinding.GetName(page, slot),
			VariableType.Numeric,
			0,
			TimeSpan.FromSeconds(1)) with
		{
			Id = VolumeMixerSliderBinding.GetId(page, slot),
			DisplayName = Strings.Variables.SliderVolume.Display(page + 1, slot + 1),
			Description = Strings.Variables.SliderVolume.Description(),
			Unit = "%",
			Write = application is { IsActive: true } ? new VariableWriteCapability() : null,
		};
	}

	private IEnumerable<VariableDefinition> BuildSliderDefinitions()
	{
		var applications = _mixer.GetApplications();
		for (var index = 0; index < applications.Count; index++)
		{
			var page = index / VolumeMixerService.SlotCount;
			var slot = index % VolumeMixerService.SlotCount;
			yield return BuildSliderDefinition(page, slot, applications[index]);
		}
	}

	private static bool TryParseVolume(object? value, out int volumePercent)
	{
		double numericValue;
		switch (value)
		{
			case JsonElement { ValueKind: JsonValueKind.Number } jsonValue when jsonValue.TryGetDouble(out numericValue):
				break;
			case byte byteValue:
				numericValue = byteValue;
				break;
			case short shortValue:
				numericValue = shortValue;
				break;
			case int intValue:
				numericValue = intValue;
				break;
			case long longValue:
				numericValue = longValue;
				break;
			case float floatValue:
				numericValue = floatValue;
				break;
			case double doubleValue:
				numericValue = doubleValue;
				break;
			case decimal decimalValue:
				numericValue = (double)decimalValue;
				break;
			default:
				volumePercent = 0;
				return false;
		}

		if (!double.IsFinite(numericValue) || numericValue is < 0 or > 100)
		{
			volumePercent = 0;
			return false;
		}

		volumePercent = (int)Math.Round(numericValue, MidpointRounding.AwayFromZero);
		return true;
	}
}
