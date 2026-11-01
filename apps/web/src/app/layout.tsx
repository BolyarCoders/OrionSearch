import type { Metadata } from "next";
import "./globals.css";
import { AppModeProvider } from "@/lib/context/app-mode-context";
import { AuthProvider } from "@/lib/context/auth-context";
import { AppShell } from "@/components/layout/shell";

export const metadata: Metadata = {
  title: "OrionSearch — Autonomous Research Archives",
  description: "Autonomous research and knowledge archiving workspace.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body className="bg-workspace text-content-primary antialiased">
        <AppModeProvider>
          <AuthProvider><AppShell>{children}</AppShell></AuthProvider>
        </AppModeProvider>
      </body>
    </html>
  );
}
