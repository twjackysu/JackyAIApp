export interface JobListing {
  id: string;
  title: string;
  company: string;
  location: string;
  salaryRange: string | null;
  url: string;
  postedAt: string; // ISO 8601
  platform: string;
  tags: string[];
  description: string | null;
}

export interface PlatformInfo {
  id: string;
  displayName: string;
}

export interface JobSearchParams {
  keywords: string;
  strategy: 'AND' | 'OR';
  platforms: string[];
}
