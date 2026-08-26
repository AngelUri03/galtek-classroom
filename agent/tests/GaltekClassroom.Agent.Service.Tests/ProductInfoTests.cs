using GaltekClassroom.Agent.Shared;

namespace GaltekClassroom.Agent.Service.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void WindowsServiceIdentity_IsStable()
    {
        Assert.Equal("GaltekClassroomAgent", ProductInfo.ServiceName);
        Assert.Equal("Galtek Classroom Agent Service", ProductInfo.ServiceDisplayName);
        Assert.Equal(
            "Servicio local de Galtek Classroom para identidad, licencia y administracion segura del equipo.",
            ProductInfo.ServiceDescription);
    }
}
