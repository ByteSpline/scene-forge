using SceneForge.Media;

namespace SceneForge.Media.Tests;

public class ModuleInfoTests
{
    [Fact]
    public void Name_IsSceneForgeMedia()
    {
        Assert.Equal("SceneForge.Media", ModuleInfo.Name);
    }
}
