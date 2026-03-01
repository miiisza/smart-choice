export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
}

export interface AuthTokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  tokenType: string;
}

export interface GuestTokenResponse {
  guestToken: string;
  expiresAt: string;
  tokenType: string;
}

export interface PollPhotoDto {
  id: number;
  photoUrl: string;
  thumbnailUrl: string | null;
  displayOrder: number;
}

export interface PollDto {
  id: number;
  authorUserId: number;
  question: string;
  status: number;
  latitude: number;
  longitude: number;
  radiusMeters: number;
  startsAt: string | null;
  endsAt: string | null;
  createdAt: string;
  updatedAt: string | null;
  photos: PollPhotoDto[];
}

export interface CreatePollRequest {
  question: string;
  photoUrls: string[];
  latitude: number;
  longitude: number;
  radiusMeters: number;
  startsAt?: string;
  endsAt?: string;
}

export interface CastVoteRequest {
  pollId: number;
  pollPhotoId: number;
}

export interface VoteDto {
  id: number;
  pollId: number;
  pollPhotoId: number;
  voterUserId: number | null;
  guestTokenId: number | null;
  votedAt: string;
}

export interface PollResultOptionDto {
  pollPhotoId: number;
  photoUrl: string;
  displayOrder: number;
  voteCount: number;
  percentage: number;
}

export interface PollWinnerDto {
  pollPhotoId: number;
  photoUrl: string;
  voteCount: number;
  percentage: number;
}

export interface PollResultsDto {
  pollId: number;
  status: number;
  totalVotes: number;
  winner: PollWinnerDto | null;
  options: PollResultOptionDto[];
}
