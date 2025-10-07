namespace ClashXW.Models
{
    public record ApiDetails(
        string BaseUrl,
        string? Secret,
        string DashboardUrl
    );
}
