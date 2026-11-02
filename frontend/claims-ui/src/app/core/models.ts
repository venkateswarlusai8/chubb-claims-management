// Mirrors the API contract (backend/Claims.Api/Contracts/Contracts.cs, also published as OpenAPI at /swagger).
// The backend owns the contract; with more time these types would be generated from the OpenAPI document.

export type ClaimStatus = 'Submitted' | 'UnderReview' | 'InformationRequired' | 'Approved' | 'Rejected' | 'Settled';
export type ClaimType = 'Motor' | 'Property';
export type UserRole = 'Claimant' | 'Officer';
export type ClaimAction = 'assign' | 'requestInformation' | 'provideInformation' | 'approve' | 'reject' | 'settle';

export interface User {
  id: string;
  name: string;
  email: string;
  role: UserRole;
}

export interface Market {
  code: string;
  name: string;
  currency: string;
}

export interface ReferenceData {
  markets: Market[];
  claimTypes: ClaimType[];
  statuses: ClaimStatus[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface ClaimSummary {
  id: string;
  claimNumber: string;
  claimantName: string;
  marketCode: string;
  marketName: string;
  claimType: ClaimType;
  status: ClaimStatus;
  estimatedAmount: number;
  outstandingAmount: number;
  currency: string;
  assignedOfficerId: string | null;
  assignedOfficerName: string | null;
  submittedAt: string;
  updatedAt: string;
  ageDays: number;
  awaitingClaimant: boolean;
}

export interface InformationRequest {
  id: string;
  question: string;
  requestedByName: string;
  requestedAt: string;
  response: string | null;
  respondedAt: string | null;
  isOpen: boolean;
}

export interface ClaimHistoryEntry {
  occurredAt: string;
  action: string;
  fromStatus: ClaimStatus | null;
  toStatus: ClaimStatus;
  actorName: string;
  actorRole: UserRole;
  note: string | null;
}

export interface ClaimDetail {
  id: string;
  claimNumber: string;
  status: ClaimStatus;
  claimantId: string;
  claimantName: string;
  claimantEmail: string;
  policyNumber: string;
  marketCode: string;
  marketName: string;
  currency: string;
  claimType: ClaimType;
  incidentDate: string;
  description: string;
  estimatedAmount: number;
  approvedAmount: number | null;
  settledAmount: number | null;
  outstandingAmount: number;
  decisionReason: string | null;
  assignedOfficerId: string | null;
  assignedOfficerName: string | null;
  submittedAt: string;
  updatedAt: string;
  decidedAt: string | null;
  settledAt: string | null;
  ageDays: number;
  version: number;
  informationRequests: InformationRequest[];
  history: ClaimHistoryEntry[];
  allowedActions: ClaimAction[];
}

export interface SubmitClaimRequest {
  policyNumber: string;
  marketCode: string;
  claimType: ClaimType;
  incidentDate: string;
  description: string;
  estimatedAmount: number;
}

export interface ClaimListQuery {
  status?: ClaimStatus;
  open?: boolean;
  assigned?: 'me' | 'unassigned';
  market?: string;
  search?: string;
  sort?: 'newest' | 'oldest';
  page?: number;
  pageSize?: number;
}

export interface StatusCount {
  status: ClaimStatus;
  count: number;
}

export interface AgingBucket {
  label: string;
  count: number;
}

export interface OfficerWorkload {
  officerId: string;
  officerName: string;
  openAssigned: number;
  underReview: number;
  awaitingClaimant: number;
  awaitingSettlement: number;
  decidedLast30Days: number;
  settledLast30Days: number;
  averageDaysToDecision: number | null;
}

export interface Workload {
  generatedAt: string;
  totalClaims: number;
  openClaims: number;
  unassignedClaims: number;
  myOpenClaims: number;
  slaDays: number;
  slaBreaches: number;
  byStatus: StatusCount[];
  aging: AgingBucket[];
  officers: OfficerWorkload[];
  oldestUnassigned: ClaimSummary[];
}

export interface MarketExposure {
  marketCode: string;
  marketName: string;
  currency: string;
  openClaims: number;
  estimatedOutstanding: number;
  approvedAwaitingSettlement: number;
  totalOutstanding: number;
  totalOutstandingReporting: number;
}

export interface ClaimTypeExposure {
  claimType: ClaimType;
  openClaims: number;
  totalOutstandingReporting: number;
}

export interface Exposure {
  generatedAt: string;
  reportingCurrency: string;
  openClaims: number;
  totalOutstanding: number;
  estimatedOutstanding: number;
  approvedAwaitingSettlement: number;
  settledLast30Days: number;
  byMarket: MarketExposure[];
  byClaimType: ClaimTypeExposure[];
  largestOpenClaims: ClaimSummary[];
  basis: string;
}

export interface AppNotification {
  id: string;
  claimId: string;
  claimNumber: string;
  title: string;
  message: string;
  createdAt: string;
  isRead: boolean;
}

export interface NotificationList {
  items: AppNotification[];
  unreadCount: number;
}

export const STATUS_LABELS: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  InformationRequired: 'Information required',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Settled: 'Settled'
};
