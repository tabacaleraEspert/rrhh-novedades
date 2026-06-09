using RRHHNovedades.Web.Models;
using Xunit;

namespace RRHHNovedades.Tests;

public class SmokeTests
{
    [Fact]
    public void Roles_DefineAdminYOperador()
    {
        Assert.Equal("Admin", Roles.Admin);
        Assert.Equal("Operador", Roles.Operador);
    }
}
