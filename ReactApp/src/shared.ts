import type { SonosDashboardQuery, SonosSchedulesQuery } from "./gql/graphql";
export type NetworkGroup = SonosDashboardQuery["groups"]["items"][number];
export type Speaker = NetworkGroup["speakers"][number];
export type Schedule = SonosSchedulesQuery["schedules"]["items"][number];
export type RunAction = (task: () => Promise<unknown>, message?: string) => Promise<boolean>;
export const modes = [
  ["PlayAll", "Play the selection", "All tracks in the folder, including its subfolders."],
  ["RandomTrack", "Random track", "One track chosen from this folder and all subfolders."],
  ["DayOfYearTrack", "Track by day of year", "Progress through the tracks from January to December."],
  ["RandomFolder", "Random subfolder", "Choose a nonempty child folder and play its contents."],
  ["DayOfYearFolder", "Subfolder by day of year", "Progress through child folders from January to December."],
] as const;
export const days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
export function dateTime(value?: string | null) {
  return value ? new Date(value).toLocaleString() : "Never";
}
export function stateLabel(speaker: Speaker) {
  return speaker.isOnline ? speaker.state.toLowerCase().replaceAll("_", " ") : "Offline";
}
