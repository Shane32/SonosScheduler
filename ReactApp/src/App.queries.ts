import { gql } from "@shane32/graphql";

gql`
  query SonosDashboard {
    session {
      isAdmin
      name
    }
    groups(first: 100) {
      items {
        id
        name
        subnet
        seedAddress
        lastRefreshedAt
        lastError
        speakers {
          id
          groupId
          sonosId
          name
          address
          coordinatorId
          isOnline
          state
          title
          artist
          album
          volume
          muted
          lastSeenAt
          statusUpdatedAt
        }
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
`;
gql`
  query SonosSchedules($groupId: ID!, $after: ID) {
    schedules(groupId: $groupId, first: 100, after: $after) {
      items {
        id
        groupId
        name
        daysOfWeek
        localTime
        timeZoneId
        enabled
        disabledUntil
        libraryObjectId
        libraryTitle
        selectionMode
        onlyIfIdle
        updatedAt
        speakers {
          speakerId
          enabled
          volume
        }
        exceptions {
          id
          date
          reason
        }
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
`;
gql`
  query SonosLibrary($groupId: ID!, $objectId: String!, $start: Int!, $count: Int!) {
    library(groupId: $groupId, objectId: $objectId, start: $start, count: $count) {
      items {
        id
        title
        artist
        album
        isContainer
      }
      numberReturned
      totalMatches
    }
  }
`;
gql`
  query SonosAdmins {
    admins(first: 100) {
      items {
        id
        tenantId
        objectId
      }
    }
  }
`;
gql`
  query SonosLogs($after: ID) {
    logs(first: 50, after: $after) {
      items {
        id
        timestamp
        level
        category
        message
        actor
        details
        scheduleId
      }
      pageInfo {
        hasNextPage
        endCursor
      }
    }
  }
`;
gql`
  mutation SonosAddGroup($input: GroupInput!) {
    groups {
      add(input: $input) {
        id
      }
    }
  }
`;
gql`
  mutation SonosUpdateGroup($id: ID!, $input: GroupInput!) {
    groups {
      update(id: $id, input: $input) {
        id
      }
    }
  }
`;
gql`
  mutation SonosDeleteGroup($id: ID!) {
    groups {
      delete(id: $id)
    }
  }
`;
gql`
  mutation SonosRefreshGroup($id: ID!) {
    groups {
      refresh(id: $id)
    }
  }
`;
gql`
  mutation SonosRefreshSpeaker($id: ID!) {
    speakers {
      refresh(id: $id)
    }
  }
`;
gql`
  mutation SonosControl($id: ID!, $action: String!, $volume: Int) {
    speakers {
      control(id: $id, action: $action, volume: $volume)
    }
  }
`;
gql`
  mutation SonosSetMembers($coordinatorId: ID!, $speakerIds: [ID!]!) {
    speakers {
      setMembers(coordinatorId: $coordinatorId, speakerIds: $speakerIds)
    }
  }
`;
gql`
  mutation SonosPlay($id: ID!, $objectId: String!, $selectionMode: String!) {
    speakers {
      play(id: $id, objectId: $objectId, selectionMode: $selectionMode)
    }
  }
`;
gql`
  mutation SonosAddSchedule($input: ScheduleInput!) {
    schedules {
      add(input: $input) {
        id
      }
    }
  }
`;
gql`
  mutation SonosUpdateSchedule($id: ID!, $input: ScheduleInput!) {
    schedules {
      update(id: $id, input: $input) {
        id
      }
    }
  }
`;
gql`
  mutation SonosDeleteSchedule($id: ID!) {
    schedules {
      delete(id: $id)
    }
  }
`;
gql`
  mutation SonosEnableSchedule($id: ID!, $enabled: Boolean!) {
    schedules {
      setEnabled(id: $id, enabled: $enabled)
    }
  }
`;
gql`
  mutation SonosPauseSchedule($id: ID!, $days: Int!) {
    schedules {
      disableFor(id: $id, days: $days)
    }
  }
`;
gql`
  mutation SonosAddAdmin($objectId: ID!) {
    admins {
      add(objectId: $objectId) {
        id
      }
    }
  }
`;
gql`
  mutation SonosDeleteAdmin($id: ID!) {
    admins {
      delete(id: $id)
    }
  }
`;
