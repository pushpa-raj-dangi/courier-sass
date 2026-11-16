import { redirect } from "next/navigation";

// F15 R3b-2: the live map is now the Map tab on /riders. Old links and bookmarks land there.
export default function LiveMapRedirect({ searchParams }: { searchParams: { branch?: string | string[] } }) {
  const branch = typeof searchParams.branch === "string" ? searchParams.branch : undefined;
  const params = new URLSearchParams({ tab: "map" });
  if (branch) params.set("branch", branch);
  redirect(`/riders?${params.toString()}`);
}
