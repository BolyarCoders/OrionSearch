import React from "react";
import Link from "next/link";
import { ChevronRight } from "lucide-react";
import { CreateArchiveStepper } from "@/components/create-archive/create-archive-stepper";

export default function NewArchivePage() {
  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="space-y-2">
        <nav className="flex items-center gap-2 text-xs text-content-muted">
          <Link href="/archives" className="hover:text-content-primary transition-colors">
            Archives
          </Link>
          <ChevronRight className="w-3.5 h-3.5" />
          <span className="text-content-primary font-medium">Create archive</span>
        </nav>

        <h1 className="library-title">
          Create a new archive
        </h1>
        <p className="text-xs sm:text-sm text-content-secondary max-w-2xl leading-relaxed">
          Define the topic, sources and update preferences. You can always change these later.
        </p>
      </div>

      {/* Stepper Form */}
      <CreateArchiveStepper />
    </div>
  );
}
