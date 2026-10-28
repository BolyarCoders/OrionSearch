from api import main


def test_terminal_jobs_are_evicted_to_make_room(monkeypatch):
    monkeypatch.setattr(main, "MAX_JOBS", 2)
    main.jobs.clear()
    main.jobs.update(
        {
            "old": {"job_id": "old", "status": "completed"},
            "active": {"job_id": "active", "status": "running"},
        }
    )

    assert main._make_job_room() is True
    assert list(main.jobs) == ["active"]
    main.jobs.clear()


def test_only_active_jobs_at_capacity_reject_new_job(monkeypatch):
    monkeypatch.setattr(main, "MAX_JOBS", 2)
    main.jobs.clear()
    main.jobs.update(
        {
            "queued": {"job_id": "queued", "status": "queued"},
            "running": {"job_id": "running", "status": "running"},
        }
    )

    assert main._make_job_room() is False
    assert set(main.jobs) == {"queued", "running"}
    main.jobs.clear()
