from __future__ import annotations

import re
from dataclasses import dataclass


@dataclass(frozen=True)
class Diagnostic:
    code: str
    title: str
    evidence: str
    next_steps: tuple[str, ...]


def diagnose(text: str) -> list[Diagnostic]:
    normalized = text.replace("\\", "/")
    lowered = normalized.lower()
    findings: list[Diagnostic] = []

    if "xboxmedia/strings.h" in lowered:
        findings.append(
            Diagnostic(
                code="missing-xbox-media",
                title="The Xbox 360 media tree is incomplete",
                evidence="The compiler requested XboxMedia/strings.h, but that generated/platform media header is absent.",
                next_steps=(
                    "Confirm the source root contains the complete XboxMedia folder from the same authorized source baseline.",
                    "Do not substitute DurangoMedia, Windows64Media, or a header from another title update.",
                    "Restore the missing media/content output from your own authorized source environment, then clean and rebuild Release|Xbox 360.",
                ),
            )
        )

    loopback_hits = len(re.findall(r"remote\s*=\s*127\.0\.0\.1", lowered))
    physical_console = any(term in lowered for term in ("real xbox", "physical xbox", "rgh", "jtag", "xbox 360"))
    if loopback_hits and physical_console:
        findings.append(
            Diagnostic(
                code="physical-console-loopback",
                title="The physical console is not reaching the relay",
                evidence=f"The relay log contains {loopback_hits} connection(s) from 127.0.0.1 and none proves a LAN console connection.",
                next_steps=(
                    "Set the compiled relay host to the relay PC's numeric LAN IPv4 address, not 127.0.0.1.",
                    "Apply the configuration before rebuilding the Xbox 360 target; changing the patcher field does not modify an already-built XEX.",
                    "Listen on 0.0.0.0:61000 and allow inbound TCP 61000 on the PC's Private firewall profile.",
                    "From another LAN device, test that the PC's LAN IPv4 and TCP 61000 are reachable.",
                ),
            )
        )

    sessions = {
        value.strip()
        for value in re.findall(r"session\s*[=:]\s*([^\r\n]+?)(?=\s+(?:build|protocol|remote|role|error|reason)\s*[=:]|$)", text, re.I)
        if value.strip()
    }
    if len(sessions) > 1:
        findings.append(
            Diagnostic(
                code="session-mismatch",
                title="The logs contain different session IDs",
                evidence="Observed session values: " + ", ".join(sorted(sessions)),
                next_steps=(
                    "Use exactly the same session ID on the host and every joining client, including spaces and punctuation.",
                    "Rebuild console clients after changing compiled relay defaults.",
                ),
            )
        )

    join_stall_phrases = (
        "infinity loading",
        "infinite loading",
        "infinitely loads",
        "infinitely loading",
        "endless loading",
        "connecting to host",
    )
    join_is_stalled = any(phrase in lowered for phrase in join_stall_phrases)
    is_xenia = "xenia" in lowered

    if join_is_stalled and is_xenia:
        findings.append(
            Diagnostic(
                code="xenia-profile-join-stall",
                title="Check the active Xenia profile before changing relay settings",
                evidence="An endless join on Xenia can be caused by its selected or signed-in profile state even when the relay is working.",
                next_steps=(
                    "Close Minecraft, select one valid Xenia profile, and confirm that profile is signed in before launching the game again.",
                    "Restart Xenia after changing profiles; do not switch profiles while Minecraft is already running.",
                    "If the same profile still stalls, keep it backed up and retry with a fresh Xenia-generated profile.",
                    "Only continue with relay diagnosis if the fresh signed-in profile also stalls.",
                ),
            )
        )

    if join_is_stalled:
        findings.append(
            Diagnostic(
                code="join-stall",
                title="The join is stalling before gameplay",
                evidence="The report describes an endless joining/loading state.",
                next_steps=((
                    "Verify the selected Xenia profile is signed in before changing relay or build settings.",
                ) if is_xenia else ()) + (
                    "Verify the relay logs show one hosting peer and one joining peer in the same session.",
                    "Verify every client uses build 584111F7-1.0.10.0-lce1.2.3-net495-proto39 and relay protocol V2.",
                    "Start the relay, enter the PC online world completely, then launch and join from the console.",
                    "Attach the relay lines covering the join attempt and LegacyRelayUserConfig.h with any token redacted.",
                ),
            )
        )

    if "0.0.0.0" in lowered:
        findings.append(
            Diagnostic(
                code="bind-address-note",
                title="0.0.0.0 is only a listener bind address",
                evidence="The relay is listening on all PC interfaces.",
                next_steps=(
                    "Do not configure a client to connect to 0.0.0.0; physical clients need the relay PC's LAN IPv4 address.",
                ),
            )
        )

    return findings


def format_diagnostics(findings: list[Diagnostic]) -> str:
    if not findings:
        return "No deterministic signature matched. Diagnose from the supplied context and request the smallest missing evidence."

    blocks: list[str] = []
    for finding in findings:
        steps = "\n".join(f"- {step}" for step in finding.next_steps)
        blocks.append(
            f"[{finding.code}] {finding.title}\n"
            f"Evidence: {finding.evidence}\n"
            f"Recommended checks:\n{steps}"
        )
    return "\n\n".join(blocks)
