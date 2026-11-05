using System;
using System.Threading;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point for the local player's side of trips between worlds (Docs/WorldStreaming.md: Travelling
/// between worlds). It asks the <see cref="ITripAuthority"/> for a trip when the local player steps onto a loaded
/// <see cref="WorldExit"/> of its world, through an <see cref="ExitTriggerGuard"/> so an exit fires once per step onto
/// it and never right after a trip. When the authority approves a trip of the local player, it stops the player and
/// runs the trip's local transition, <see cref="GameFlow.TravelAsync"/>: fade out with input off, arrive at the entry
/// once the ground there is loaded, fade in, with game time running all along; then it reports the trip over
/// (<see cref="ITripAuthority.ReportArrived"/>), which ends the player's protection. It waits for any other transition to
/// end before starting, and checks nothing while one runs. Each frame it only compares the local player's cell with the
/// last one.
/// </summary>
public sealed class LocalTripTraveller : IStartable, ITickable, IDisposable
{
    private readonly LocalPlayerTracker localPlayer;
    private readonly PlayerWorlds playerWorlds;
    private readonly WorldExits exits;
    private readonly ITripAuthority authority;
    private readonly TripArrivals arrivals;
    private readonly GameFlow gameFlow;
    private readonly ExitTriggerGuard guard = new();
    private readonly CancellationTokenSource disposeCancellation = new();
    private WorldDefinition lastWorld;
    private Vector2Int lastCell;
    private bool stale = true;
    private bool travelling;

    /// <summary>
    /// Captures what the trigger reads and where requests go.
    /// </summary>
    public LocalTripTraveller(
        LocalPlayerTracker localPlayer,
        PlayerWorlds playerWorlds,
        WorldExits exits,
        ITripAuthority authority,
        TripArrivals arrivals,
        GameFlow gameFlow)
    {
        this.localPlayer = localPlayer;
        this.playerWorlds = playerWorlds;
        this.exits = exits;
        this.authority = authority;
        this.arrivals = arrivals;
        this.gameFlow = gameFlow;
    }

    public bool IsTravelling => travelling;

    /// <summary>
    /// The guard, for tests that check re-triggering.
    /// </summary>
    internal ExitTriggerGuard Guard => guard;

    /// <summary>
    /// Listens for approved trips.
    /// </summary>
    public void Start()
    {
        authority.Approved += HandleApproved;
    }

    /// <summary>
    /// Stops listening and cancels a trip waiting to start.
    /// </summary>
    public void Dispose()
    {
        authority.Approved -= HandleApproved;
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    /// <summary>
    /// When the local player, alive and in a world, stands on a new cell outside any transition, tells the guard
    /// whether that cell is an exit's and asks for the trip when the guard fires.
    /// </summary>
    public void Tick()
    {
        if (travelling || gameFlow.IsTransitioning)
        {
            return;
        }

        LocalPlayer local = localPlayer.Current;
        if (local == null || !local.Handle.IsAlive)
        {
            return;
        }

        WorldDefinition world = playerWorlds.Get(local.Handle);
        if (world == null)
        {
            lastWorld = null;
            return;
        }

        Vector2Int cell = ChunkCoords.CellOf(world.ToLocal(local.Transform.position));
        if (!stale && world == lastWorld && cell == lastCell)
        {
            return;
        }

        stale = false;
        lastWorld = world;
        lastCell = cell;
        bool onExit = exits.TryFindAt(world, cell, out WorldExit exit);
        if (guard.Step(onExit))
        {
            authority.Request(new TripRequest(local.Handle, exit.Id));
        }
    }

    private void HandleApproved(TripApproval approval)
    {
        LocalPlayer local = localPlayer.Current;
        if (local != null && approval.Player == local.Handle)
        {
            _ = GoAsync(local, approval, disposeCancellation.Token);
        }
    }

    /// <summary>
    /// Waits for any running transition to end, then stops the player and runs the trip's transition, and once it has
    /// faded in reports the trip's end to the authority, which ends the protection the trip gave the player. A session
    /// that ended meanwhile drops the trip's arrival instead. The guard waits for the player to step off any exit
    /// afterwards.
    /// </summary>
    private async Awaitable GoAsync(LocalPlayer local, TripApproval approval, CancellationToken cancellation)
    {
        travelling = true;
        guard.Disarm();
        int trip = playerWorlds.TripOf(approval.Player);
        try
        {
            while (gameFlow.IsTransitioning)
            {
                await Awaitable.NextFrameAsync(cancellation);
            }

            if (!gameFlow.IsInGame || gameFlow.CurrentWorld == null || local.Controller == null)
            {
                arrivals.Release(approval.Player);
                return;
            }

            local.Controller.ClearOrders();
            await gameFlow.TravelAsync(approval.World, approval.EntryName, cancellation);
            if (!cancellation.IsCancellationRequested)
            {
                authority.ReportArrived(approval.Player, trip);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            travelling = false;
            stale = true;
            guard.Disarm();
        }
    }
}
