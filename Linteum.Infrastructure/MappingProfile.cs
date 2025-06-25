using AutoMapper;
using Linteum.Domain;
using Linteum.Shared.DTO;

namespace Linteum.Infrastructure
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<UserDto, User>();
            CreateMap<User, UserDto>();
            CreateMap<CanvasDto, Canvas>();
            CreateMap<Canvas, CanvasDto>();
            CreateMap<ColorDto, Color>();
            CreateMap<Color, ColorDto>();
            CreateMap<SubscriptionDto, Subscription>();
            CreateMap<Subscription, SubscriptionDto>();
            CreateMap<BalanceChangedEvent, BalanceChangedEventDto>();
            CreateMap<BalanceChangedEventDto, BalanceChangedEvent>();
            CreateMap<LoginEvent, LoginEventDto>();
            CreateMap<LoginEventDto, LoginEvent>();
            CreateMap<PixelChangedEvent, PixelChangedEventDto>();
            CreateMap<PixelChangedEventDto, PixelChangedEvent>();
            CreateMap<Pixel, PixelDto>();
            CreateMap<PixelDto, Pixel>();
        }
    }
}