using AutoMapper;

namespace Linteum.Infrastructure;

public class TestMapper
{
    private static IMapper? _instance;
    public static IMapper Instance => _instance ??= new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<MappingProfile>();
    }).CreateMapper();
}