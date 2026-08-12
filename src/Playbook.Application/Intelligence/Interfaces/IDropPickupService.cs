using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Intelligence.Interfaces;

/// <summary>
/// Drop/Pickup intelligence for the currently selected league + owned team. Additive to
/// Start/Sit and Quick Picks — does not read or write any Decision-engine state.
/// </summary>
public interface IDropPickupService
{
    DropPickupReport GetReport();
}
