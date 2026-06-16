using RRHHNovedades.Web.Models;
using Xunit;

namespace RRHHNovedades.Tests;

public class SmokeTests
{
    [Fact]
    public void Roles_DefineAdminYRRHH()
    {
        Assert.Equal("Admin", Roles.Admin);
        Assert.Equal("RRHH", Roles.RRHH);
    }
}
