using System.Xml.Linq;
using FluentAssertions;

namespace MultiMessenger.Tests.Architecture;

/// <summary>
/// Закрепляет направление зависимостей из п. 1.1 плана: Web → Infrastructure → Core,
/// при этом Core не зависит ни от чего. Проверка идёт по .csproj, а не по скомпилированным
/// сборкам: список referenced assemblies содержит только реально использованные ссылки,
/// поэтому «лишний» пакет в проекте без кода такой тест бы не заметил.
/// </summary>
public class ProjectDependencyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void CoreProjectHasNoDependencies()
    {
        var core = LoadProject("src/MultiMessenger.Core/MultiMessenger.Core.csproj");

        ReferencedPackages(core).Should().BeEmpty(
            "Core — доменный слой, он не должен зависеть от EF Core, ASP.NET или клиентов мессенджеров");
        ReferencedProjects(core).Should().BeEmpty(
            "Core находится в основании графа зависимостей");
    }

    [Fact]
    public void InfrastructureDependsOnCoreOnly()
    {
        var infrastructure = LoadProject("src/MultiMessenger.Infrastructure/MultiMessenger.Infrastructure.csproj");

        ReferencedProjects(infrastructure).Should().Equal("MultiMessenger.Core");
    }

    [Fact]
    public void WebDependsOnInfrastructureOnly()
    {
        var web = LoadProject("src/MultiMessenger.Web/MultiMessenger.Web.csproj");

        ReferencedProjects(web).Should().Equal(new[] { "MultiMessenger.Infrastructure" },
            "доступ к Core у Web транзитивный — прямая ссылка ломает заявленную цепочку слоёв");
    }

    private static XDocument LoadProject(string relativePath)
    {
        var fullPath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(fullPath).Should().BeTrue($"проект {relativePath} должен существовать");

        return XDocument.Load(fullPath);
    }

    private static IEnumerable<string> ReferencedPackages(XDocument project) =>
        project.Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .Order();

    private static IEnumerable<string> ReferencedProjects(XDocument project) =>
        project.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")?.Value ?? string.Empty))
            .Order();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MultiMessenger.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException(
                   $"Не найден корень репозитория (MultiMessenger.slnx) выше {AppContext.BaseDirectory}");
    }
}
