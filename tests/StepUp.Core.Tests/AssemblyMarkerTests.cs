using StepUp.Core;

namespace StepUp.Core.Tests;

public class AssemblyMarkerTests
{
    [Fact]
    public void Core_assembly_is_named_StepUp_Core()
    {
        Assert.Equal("StepUp.Core", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
