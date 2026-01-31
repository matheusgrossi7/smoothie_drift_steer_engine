using DriftCore.Models;
using DriftCore.Services.Input;

namespace DriftCore.Services.Status;

/// <summary>
/// Constrói o status da engine de forma leve e previsível.
/// </summary>
public sealed class EngineStatusProvider
{
    private readonly List<GameProfile> _implementedGamesSnapshot;
    private readonly HashSet<GameProfile> _implementedGamesSet;
    private readonly GameProfile[] _allGames;

    public EngineStatusProvider(IReadOnlyList<GameProfile> implementedGames)
    {
        _implementedGamesSnapshot = implementedGames.ToList();
        _implementedGamesSet = new HashSet<GameProfile>(_implementedGamesSnapshot);
        _allGames = Enum.GetValues<GameProfile>();
    }

    public EngineStatus BuildStatus(InputDeviceType selectedInput, GameProfile selectedGame)
    {
        var inputs = new List<DeviceInfo>(4);
        DeviceInfo? selected = null;

        for (int i = 0; i < 4; i++)
        {
            var device = new DeviceInfo
            {
                Name = $"Xbox Controller {i + 1}",
                Type = (InputDeviceType)i,
                IsConnected = GamepadReader.IsConnected(i)
            };

            if (device.Type == selectedInput)
                selected = device;

            inputs.Add(device);
        }

        var notImplemented = new List<GameProfile>(_allGames.Length);
        foreach (var game in _allGames)
        {
            if (!_implementedGamesSet.Contains(game))
                notImplemented.Add(game);
        }

        return new EngineStatus
        {
            AvailableInputList = inputs,
            SelectedInput = selected ?? new DeviceInfo { Name = "None", IsConnected = false },
            ImplementedGames = _implementedGamesSnapshot,
            NotImplementedGames = notImplemented,
            SelectedGame = new GameInfo
            {
                Game = selectedGame,
                IsConnected = false // Fase 1: Sem telemetria
            }
        };
    }
}