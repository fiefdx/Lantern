using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Sanity test that the test project references the app assembly (T0.2).
/// </summary>
public class SolutionSmokeTests
{
    [Fact]
    public void AppAssemblyIsLoadable()
    {
        var type = typeof(Lantern.UI.StartupForm);
        Assert.Equal("Lantern", type.Assembly.GetName().Name);
    }
}
