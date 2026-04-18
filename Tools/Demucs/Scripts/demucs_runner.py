import argparse
import json
import sys


def _import_directml():
    try:
        import torch_directml
    except Exception as exc:  # pragma: no cover - runtime bootstrap only
        raise RuntimeError(f"torch_directml import failed: {exc}") from exc

    return torch_directml


def _run_probe_cuda():
    import torch

    is_available = bool(torch.cuda.is_available())
    device_count = int(torch.cuda.device_count()) if is_available else 0
    payload = {
        "isAvailable": is_available,
        "deviceCount": device_count,
        "defaultDevice": int(torch.cuda.current_device()) if device_count > 0 else None,
        "devices": [
            {
                "index": index,
                "name": torch.cuda.get_device_name(index),
                "device": f"cuda:{index}",
            }
            for index in range(device_count)
        ],
    }

    print(json.dumps(payload, ensure_ascii=False))
    return 0


def _run_probe_directml():
    torch_directml = _import_directml()
    device_count = int(torch_directml.device_count())
    payload = {
        "isAvailable": bool(torch_directml.is_available()),
        "deviceCount": device_count,
        "defaultDevice": int(torch_directml.default_device()) if device_count > 0 else None,
        "devices": [
            {
                "index": index,
                "name": torch_directml.device_name(index),
                "device": str(torch_directml.device(index)),
            }
            for index in range(device_count)
        ],
    }

    print(json.dumps(payload, ensure_ascii=False))
    return 0


def _run_separate(demucs_args):
    device_argument = None

    for index, value in enumerate(demucs_args):
        if value in ("-d", "--device") and index + 1 < len(demucs_args):
            device_argument = demucs_args[index + 1]
            break

    if device_argument and device_argument.lower().startswith("privateuseone"):
        _import_directml()

    from demucs.separate import main as demucs_main

    demucs_main(demucs_args)
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser("vidvix_demucs_runner")
    subparsers = parser.add_subparsers(dest="command", required=True)

    subparsers.add_parser("probe")
    subparsers.add_parser("probe-cuda")
    subparsers.add_parser("probe-directml")

    separate_parser = subparsers.add_parser("separate")
    separate_parser.add_argument("demucs_args", nargs=argparse.REMAINDER)

    args, remaining = parser.parse_known_args(argv)

    if args.command == "probe-cuda":
        return _run_probe_cuda()

    if args.command in ("probe", "probe-directml"):
        return _run_probe_directml()

    demucs_args = list(args.demucs_args)
    if remaining:
        demucs_args.extend(remaining)

    if demucs_args and demucs_args[0] == "--":
        demucs_args = demucs_args[1:]

    return _run_separate(demucs_args)


if __name__ == "__main__":
    sys.exit(main())
