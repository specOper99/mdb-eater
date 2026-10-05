using MdbConverter.Core.Access;
using MdbConverter.Core.Models;

namespace MdbConverter.Access;

public static class MdbSessionFactory
{
    public static IMdbSession Open(OpenOptions options) => new AceMdbSession(options);
}
