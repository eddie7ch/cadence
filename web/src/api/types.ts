/**
 * Mirrors src/Cadence.Application/Contracts/Dtos.cs. The API serialises with
 * camelCase property names and enums as strings, so every enum below is a
 * string union rather than a numeric one.
 */

export const SPORTS = [
  'Unknown',
  'Running',
  'TrailRunning',
  'Cycling',
  'MountainBiking',
  'Swimming',
  'Walking',
  'Hiking',
  'Rowing',
  'Skiing',
] as const;

export type Sport = (typeof SPORTS)[number];

export const ACTIVITY_STATUSES = ['Pending', 'Processing', 'Ready', 'Failed'] as const;

export type ActivityStatus = (typeof ACTIVITY_STATUSES)[number];

export type TrainingLoadVerdict =
  | 'Unknown'
  | 'Detraining'
  | 'Maintaining'
  | 'Productive'
  | 'Overreaching';

export interface AthleteDto {
  id: string;
  email: string;
  displayName: string;
  maxHeartRate: number | null;
  restingHeartRate: number | null;
  createdAt: string;
}

export interface AuthResponseDto {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  athlete: AthleteDto;
}

export interface ActivitySummaryDto {
  id: string;
  name: string;
  sport: Sport;
  status: ActivityStatus;
  startedAt: string;
  distanceMeters: number;
  movingSeconds: number;
  elapsedSeconds: number;
  elevationGainMeters: number;
  paceSecondsPerKm: number;
  gradeAdjustedPaceSecondsPerKm: number;
  averageHeartRateBpm: number | null;
  error: string | null;
}

export interface SplitDto {
  number: number;
  distanceMeters: number;
  durationSeconds: number;
  paceSecondsPerKm: number;
  gradeAdjustedPaceSecondsPerKm: number;
  elevationGainMeters: number;
  averageHeartRateBpm: number | null;
  isComplete: boolean;
}

/**
 * Coordinates are GeoJSON order - [longitude, latitude] - and the bounding box
 * is [minLon, minLat, maxLon, maxLat]. Leaflet wants the opposite order, so
 * nothing may hand these arrays to a map component unconverted.
 */
export interface RouteDto {
  coordinates: number[][];
  boundingBox: number[];
  pointCount: number;
  simplifiedPointCount: number;
}

export interface ActivityDetailDto {
  summary: ActivitySummaryDto;
  route: RouteDto | null;
  splits: SplitDto[];
  heartRateZoneSeconds: Record<string, number>;
  sampleCount: number;
  discardedSampleCount: number;
}

/** Column-oriented: one array per channel, all of the same length. */
export interface TimeSeriesDto {
  elapsedSeconds: number[];
  distanceMeters: number[];
  altitudeMeters: (number | null)[];
  heartRateBpm: (number | null)[];
  speedMetersPerSecond: (number | null)[];
  cadenceRpm: (number | null)[];
  powerWatts: (number | null)[];
  resolution: number;
}

export interface WeeklyTotalsDto {
  /** ISO date (yyyy-MM-dd): DateOnly serialises without a time component. */
  weekStart: string;
  activityCount: number;
  distanceMeters: number;
  elevationGainMeters: number;
  movingSeconds: number;
  averageHeartRateBpm: number | null;
}

export interface TrendsDto {
  weeks: WeeklyTotalsDto[];
  totalDistanceMeters: number;
  totalElevationGainMeters: number;
  totalMovingSeconds: number;
  totalActivities: number;
  distanceTrendPercent: number;
}

export interface CoachingFindingDto {
  title: string;
  detail: string;
  metric: string;
  severity: string;
}

export interface CoachingReportDto {
  id: string;
  periodStart: string;
  periodEnd: string;
  summary: string;
  verdict: TrainingLoadVerdict;
  findings: CoachingFindingDto[];
  activityCount: number;
  modelId: string;
  generatedAt: string;
}

export interface NearbyActivityDto {
  id: string;
  name: string;
  sport: Sport;
  startedAt: string;
  distanceMeters: number;
  paceSecondsPerKm: number;
}

export interface PagedDto<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** RFC 7807. `errors` is present on ASP.NET Core model-validation failures. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
}
