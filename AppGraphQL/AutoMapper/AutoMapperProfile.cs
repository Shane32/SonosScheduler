using AppGraphQL.InputModels;
using AutoMapper;

namespace AppGraphQL.AutoMapper;

/// <summary>
/// AutoMapper profile for mapping between GraphQL input types and database models.
/// </summary>
public class AutoMapperProfile : Profile
{
    public AutoMapperProfile(TimeProvider timeProvider)
    {
        CreateMap<GroupInput, NetworkGroup>(MemberList.Source);
        CreateMap<ScheduleSpeakerInput, ScheduleSpeaker>(MemberList.Source);
        CreateMap<ScheduleExclusionInput, ScheduleExclusion>(MemberList.Source);
        CreateMap<ScheduleInput, Schedule>(MemberList.Source)
            .ForMember(x => x.Speakers, o => o.Ignore())
            .ForMember(x => x.Exceptions, o => o.Ignore())
            .ForMember(x => x.UpdatedAt, o => o.MapFrom(_ => timeProvider.GetUtcNow()));
    }
}
