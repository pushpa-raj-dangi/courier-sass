using Courier.Geography.Domain;

namespace Courier.Geography.Application;

public record ProvinceResponse(Guid Id, string Name);

public record DistrictResponse(Guid Id, Guid ProvinceId, string Name);

public record LocalLevelResponse(Guid Id, Guid DistrictId, string Name, LocalLevelType Type);

public record WardResponse(Guid Id, Guid LocalLevelId, int Number);

public record AreaResponse(Guid Id, Guid WardId, string Name);
