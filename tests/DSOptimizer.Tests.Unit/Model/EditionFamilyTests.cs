using DSOptimizer.Core.Model;
using Xunit;

namespace DSOptimizer.Tests.Unit.Model;

public class EditionFamilyTests
{
    [Theory]
    [InlineData("Core", EditionFamily.Home)]
    [InlineData("CoreN", EditionFamily.Home)]
    [InlineData("CoreSingleLanguage", EditionFamily.Home)]
    [InlineData("CoreCountrySpecific", EditionFamily.Home)]
    [InlineData("Professional", EditionFamily.Pro)]
    [InlineData("ProfessionalN", EditionFamily.Pro)]
    [InlineData("ProfessionalWorkstation", EditionFamily.Pro)]
    [InlineData("ProfessionalWorkstationN", EditionFamily.Pro)]
    [InlineData("ProfessionalEducation", EditionFamily.Pro)]
    [InlineData("ProfessionalEducationN", EditionFamily.Pro)]
    [InlineData("Enterprise", EditionFamily.Business)]
    [InlineData("EnterpriseN", EditionFamily.Business)]
    [InlineData("EnterpriseS", EditionFamily.Business)]
    [InlineData("EnterpriseSN", EditionFamily.Business)]
    [InlineData("Education", EditionFamily.Business)]
    [InlineData("EducationN", EditionFamily.Business)]
    [InlineData("IoTEnterprise", EditionFamily.Business)]
    [InlineData("IoTEnterpriseS", EditionFamily.Business)]
    [InlineData("professional", EditionFamily.Pro)]
    [InlineData("CORE", EditionFamily.Home)]
    public void FromEditionId_MapsKnownIds(string id, EditionFamily expected)
        => Assert.Equal(expected, EditionFamilies.FromEditionId(id));

    [Theory]
    [InlineData("unknown")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ServerStandard")]
    public void FromEditionId_UnknownOrMissing_IsNull(string? id)
        => Assert.Null(EditionFamilies.FromEditionId(id));
}
