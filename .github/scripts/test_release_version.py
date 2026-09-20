import unittest

from check_release_version import validate_release


class ReleaseVersionTests(unittest.TestCase):
    def test_increasing_versions(self):
        for requested, tags in [('1.0.7', []), ('1.0.7', ['v1.0.6']), ('1.0.10', ['v1.0.9'])]:
            with self.subTest(requested=requested, tags=tags):
                validate_release(requested, tags)

    def test_reused_or_older_versions(self):
        for tag in ['v1.0.7', 'v1.0.8', 'v2.0.0']:
            with self.subTest(tag=tag), self.assertRaisesRegex(ValueError, 'must exceed'):
                validate_release('1.0.7', [tag])

    def test_invalid_msi_versions(self):
        for version in ['1.2', '1.2.3.4', '01.2.3', '256.0.0', '1.256.0', '1.0.65536']:
            with self.subTest(version=version), self.assertRaisesRegex(ValueError, 'Invalid release'):
                validate_release(version, [])

    def test_unrelated_tags_are_ignored(self):
        validate_release('1.0.7', ['preview', 'v9.0.0-beta'])


if __name__ == '__main__':
    unittest.main()
