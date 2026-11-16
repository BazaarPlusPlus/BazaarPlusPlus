export interface Job {
  messageId: string;
  groupId: string;
  memberId: string;
  content: string;
  timestamp: number;
}
export interface WebhookEnv {
  QQ_BOT_APP_ID: string;
  QQ_BOT_APP_SECRET: string;
  JOBS: Queue<Job>;
}
export interface Env {
  QQ_BOT_APP_ID: string;
  QQ_BOT_APP_SECRET: string;
  BOT_STATS_TOKEN: string;
  SERVER_ORIGIN: string;
  DB: D1Database;
  BROWSER: Fetcher;
}
