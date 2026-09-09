"use client";

// eSewa's flow needs a real HTML form POST with signed fields — a plain redirect can't carry a
// POST body. Khalti's flow is a plain GET redirect (its checkoutInfo.formFields is always
// empty). This one component handles both without the caller needing to know which shape it got.
export function redirectToGateway(checkoutInfo: { redirectUrl: string; formFields: Record<string, string> }) {
  if (Object.keys(checkoutInfo.formFields).length === 0) {
    window.location.href = checkoutInfo.redirectUrl;
    return;
  }

  const form = document.createElement("form");
  form.method = "POST";
  form.action = checkoutInfo.redirectUrl;
  for (const [key, value] of Object.entries(checkoutInfo.formFields)) {
    const input = document.createElement("input");
    input.type = "hidden";
    input.name = key;
    input.value = value;
    form.appendChild(input);
  }
  document.body.appendChild(form);
  form.submit();
}
