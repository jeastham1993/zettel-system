using ZettelWeb.Models;

namespace ZettelWeb.Services;

public interface IGraphService
{
    Task<GraphData> BuildGraphAsync(double semanticThreshold = 0.8);
}
