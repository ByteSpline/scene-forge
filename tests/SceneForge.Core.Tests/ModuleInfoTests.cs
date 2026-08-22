using SceneForge.Core;

namespace SceneForge.Core.Tests;

public class ModuleInfoTests
{
    [Fact]
    public void Name_IsSceneForgeCore()
    {
        Assert.Equal("SceneForge.Core", ModuleInfo.Name);
    }
}
