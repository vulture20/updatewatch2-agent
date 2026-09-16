using Microsoft.Extensions.Logging.Abstractions;
using UpdateWatch2.Agent.UpdateCheck.Windows;

namespace UpdateWatch2.Agent.Tests.UpdateCheck;

/// <summary>
/// Covers <see cref="WindowsUpdatePolicyEnforcer"/>'s own orchestration
/// logic against a hand-written fake <see cref="IWindowsUpdatePolicyStore"/>
/// — the real registry-backed <see cref="WindowsRegistryUpdatePolicyStore"/>
/// needs a real Windows host and is untested here, but the enforcer itself
/// has no Windows-specific code left, so it runs on this project's Linux
/// CI like everything else in this split.
/// </summary>
public class WindowsUpdatePolicyEnforcerTests
{
    [Fact]
    public void Writes_the_policy_when_it_was_never_configured()
    {
        var store = new FakeStore(initialValue: null);
        var enforcer = new WindowsUpdatePolicyEnforcer(store, NullLogger<WindowsUpdatePolicyEnforcer>.Instance);

        enforcer.EnsureNativeAutomaticUpdatesDisabled();

        Assert.True(store.DisableWasCalled);
    }

    [Fact]
    public void Writes_the_policy_when_it_was_explicitly_enabled()
    {
        var store = new FakeStore(initialValue: 0);
        var enforcer = new WindowsUpdatePolicyEnforcer(store, NullLogger<WindowsUpdatePolicyEnforcer>.Instance);

        enforcer.EnsureNativeAutomaticUpdatesDisabled();

        Assert.True(store.DisableWasCalled);
    }

    [Fact]
    public void Does_nothing_when_the_policy_is_already_disabled()
    {
        var store = new FakeStore(initialValue: 1);
        var enforcer = new WindowsUpdatePolicyEnforcer(store, NullLogger<WindowsUpdatePolicyEnforcer>.Instance);

        enforcer.EnsureNativeAutomaticUpdatesDisabled();

        Assert.False(store.DisableWasCalled);
    }

    [Fact]
    public void Swallows_an_exception_from_the_store_rather_than_letting_it_escape()
    {
        // Called unconditionally at agent startup (Program.cs) — a failure
        // here (e.g. no registry write permission for some reason) must
        // never prevent the agent itself from starting.
        var store = new FakeStore(initialValue: null, onDisable: () => throw new InvalidOperationException("simulated registry failure"));
        var enforcer = new WindowsUpdatePolicyEnforcer(store, NullLogger<WindowsUpdatePolicyEnforcer>.Instance);

        enforcer.EnsureNativeAutomaticUpdatesDisabled();
    }

    private class FakeStore(int? initialValue, Action? onDisable = null) : IWindowsUpdatePolicyStore
    {
        public bool DisableWasCalled { get; private set; }

        public int? GetNoAutoUpdateValue() => initialValue;

        public void DisableNativeAutomaticUpdates()
        {
            DisableWasCalled = true;
            onDisable?.Invoke();
        }
    }
}
