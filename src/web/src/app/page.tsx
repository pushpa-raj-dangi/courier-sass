import { redirect } from "next/navigation";
import { getValidAccessToken } from "@/lib/auth/session";

export default async function RootPage() {
  const accessToken = await getValidAccessToken();
  redirect(accessToken ? "/dashboard" : "/login");
}
