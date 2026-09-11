using UnityEngine;
using System.Collections;
using System.IO;
using System.Globalization;

public class DatasetCapture : MonoBehaviour
{
    // ============================================================
    // CAMERA
    // ============================================================

    [Header("Camera")]
    public Camera datasetCamera;


    // ============================================================
    // TARGET SATELLITE
    // ============================================================

    [Header("Target Satellite")]
    public Transform targetSatellite;


    // ============================================================
    // LASER
    // ============================================================

    [Header("Laser")]

    [Tooltip("The transform where the laser originates.")]
    public Transform laserOrigin;

    [Tooltip("The visible laser cylinder.")]
    public Transform laserBeam;


    // ============================================================
    // DATASET SIZE
    // ============================================================

    [Header("Dataset Size")]

    public int positiveFOVImages = 1000;

    public int negativeFOVImages = 1000;

    public int misalignedLaserImages = 1000;


    // ============================================================
    // IMAGE SETTINGS
    // ============================================================

    [Header("Image Settings")]

    public int width = 1920;

    public int height = 1080;

    [Range(1, 100)]
    public int jpegQuality = 95;


    // ============================================================
    // AUTOMATIC GENERATION
    // ============================================================

    [Header("Automatic Generation")]

    public bool automaticGeneration = true;

    public float delayBetweenImages = 0.05f;


    // ============================================================
    // SATELLITE DISTANCE
    // ============================================================

    [Header("Satellite Distance")]

    public bool randomizeDistance = true;

    public float minDistance = 8f;

    public float maxDistance = 25f;


    // ============================================================
    // SATELLITE ROTATION
    // ============================================================

    [Header("Satellite Rotation")]

    public bool randomizeRotation = true;


    // ============================================================
    // POSITIVE FOV
    // ============================================================

    [Header("Positive FOV Settings")]

    [Range(0.05f, 0.45f)]
    public float positiveHorizontalMargin = 0.30f;

    [Range(0.05f, 0.45f)]
    public float positiveVerticalMargin = 0.25f;


    // ============================================================
    // NEGATIVE FOV
    // ============================================================

    [Header("Negative FOV Settings")]

    [Tooltip("Extra angle outside the camera FOV.")]
    public float negativeFOVAngle = 15f;


    // ============================================================
    // LASER MISALIGNMENT
    // ============================================================

    [Header("Laser Misalignment")]

    public float minLaserMisalignment = 15f;

    public float maxLaserMisalignment = 45f;


    // ============================================================
    // CAMERA FOV
    // ============================================================

    [Header("Camera FOV")]

    public bool randomizeCameraFOV = true;

    public float minFOV = 35f;

    public float maxFOV = 65f;


    // ============================================================
    // CAMERA POSITION
    // ============================================================

    [Header("Camera Position Variation")]

    public bool randomizeCameraPosition = false;

    public float cameraPositionVariation = 0.5f;


    // ============================================================
    // MOTION
    // ============================================================

    [Header("Motion")]

    public bool enableMotion = false;

    public float motionAmount = 0.1f;


    // ============================================================
    // NOISE
    // ============================================================

    [Header("Noise")]

    public bool enableNoise = false;

    [Range(0f, 0.1f)]
    public float noiseStrength = 0.01f;


    // ============================================================
    // OCCLUSION
    // ============================================================

    [Header("Occlusion")]

    public bool enableOcclusion = false;

    [Range(0f, 1f)]
    public float occlusionProbability = 0.2f;

    public GameObject occluder;

    public float minOccluderScale = 0.5f;

    public float maxOccluderScale = 1.5f;


    // ============================================================
    // VALIDATION / RETRY
    // ============================================================

    [Header("Validation")]

    [Tooltip("How many times to retry building a valid scene for one image before giving up and skipping it.")]
    public int maxPlacementAttempts = 30;


    // ============================================================
    // UI
    // ============================================================

    [Header("UI")]

    public bool showCaptureUI = true;


    // ============================================================
    // INTERNAL
    // ============================================================

    private int positiveCount;
    private int negativeCount;
    private int misalignedCount;

    private bool isGenerating;

    private Coroutine generationCoroutine;


    // ============================================================
    // ORIGINAL STATE
    // ============================================================

    private Vector3 originalTargetPosition;
    private Quaternion originalTargetRotation;

    private Vector3 originalCameraPosition;
    private Quaternion originalCameraRotation;

    private float originalCameraFOV;

    private Vector3 originalLaserPosition;
    private Quaternion originalLaserRotation;

    private Vector3 originalBeamPosition;
    private Quaternion originalBeamRotation;
    private Vector3 originalBeamScale;


    // ============================================================
    // FOLDERS
    // ============================================================

    private string positiveImagesFolder;
    private string positiveLabelsFolder;

    private string negativeImagesFolder;
    private string negativeLabelsFolder;

    private string misalignedImagesFolder;
    private string misalignedLabelsFolder;


    // ============================================================
    // CATEGORY
    //
    // Declared at class scope (was previously declared inside the
    // capture method's region but used across the whole file) so
    // every method references exactly ONE definition — this removes
    // any chance of category values getting out of sync between
    // GenerateDataset(), CaptureCategory(), and SaveImageAndLabel().
    // ============================================================

    enum DatasetCategory
    {
        PositiveFOV,
        NegativeFOV,
        MisalignedLaser
    }


    // ============================================================
    // START
    // ============================================================

    void Start()
    {
        SetupFolders();

        FindNextNumbers();

        if (!ValidateReferences())
            return;

        SaveOriginalState();

        Debug.Log("======================================");
        Debug.Log("DATASET CAPTURE SYSTEM READY");
        Debug.Log("======================================");

        Debug.Log(
            "Positive FOV: " +
            positiveCount +
            " / " +
            positiveFOVImages
        );

        Debug.Log(
            "Negative FOV: " +
            negativeCount +
            " / " +
            negativeFOVImages
        );

        Debug.Log(
            "Misaligned Laser: " +
            misalignedCount +
            " / " +
            misalignedLaserImages
        );

        Debug.Log("======================================");

        if (automaticGeneration)
        {
            StartGeneration();
        }
    }


    // ============================================================
    // VALIDATION
    // ============================================================

    bool ValidateReferences()
    {
        bool valid = true;

        if (datasetCamera == null)
        {
            Debug.LogError(
                "DATASET ERROR: Dataset Camera is not assigned."
            );

            valid = false;
        }

        if (targetSatellite == null)
        {
            Debug.LogError(
                "DATASET ERROR: Target Satellite is not assigned."
            );

            valid = false;
        }

        if (laserOrigin == null)
        {
            Debug.LogError(
                "DATASET ERROR: Laser Origin is not assigned."
            );

            valid = false;
        }

        if (laserBeam == null)
        {
            Debug.LogError(
                "DATASET ERROR: Laser Beam is not assigned."
            );

            valid = false;
        }

        return valid;
    }


    // ============================================================
    // UPDATE
    // ============================================================

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            StartGeneration();
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            StopGeneration();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            if (!isGenerating)
            {
                StartCoroutine(
                    CaptureSingleImage()
                );
            }
        }
    }


    // ============================================================
    // UI
    // ============================================================

    void OnGUI()
    {
        if (!showCaptureUI)
            return;

        GUIStyle style =
            new GUIStyle(GUI.skin.label);

        style.fontSize = 18;

        GUI.Label(
            new Rect(20, 20, 500, 30),
            "Positive FOV: " +
            positiveCount +
            " / " +
            positiveFOVImages,
            style
        );

        GUI.Label(
            new Rect(20, 50, 500, 30),
            "Negative FOV: " +
            negativeCount +
            " / " +
            negativeFOVImages,
            style
        );

        GUI.Label(
            new Rect(20, 80, 500, 30),
            "Misaligned Laser: " +
            misalignedCount +
            " / " +
            misalignedLaserImages,
            style
        );

        GUI.Label(
            new Rect(20, 110, 500, 30),
            isGenerating
                ? "STATUS: GENERATING"
                : "STATUS: READY",
            style
        );

        if (!isGenerating)
        {
            if (
                GUI.Button(
                    new Rect(20, 150, 220, 50),
                    "GENERATE DATASET"
                )
            )
            {
                StartGeneration();
            }
        }
        else
        {
            if (
                GUI.Button(
                    new Rect(20, 150, 220, 50),
                    "STOP GENERATION"
                )
            )
            {
                StopGeneration();
            }
        }
    }


    // ============================================================
    // FOLDER SETUP
    // ============================================================

    void SetupFolders()
    {
        string root =
            Path.Combine(
                Application.dataPath,
                "Dataset"
            );

        positiveImagesFolder =
            Path.Combine(
                root,
                "Images/Positive_FOV"
            );

        positiveLabelsFolder =
            Path.Combine(
                root,
                "Labels/Positive_FOV"
            );

        negativeImagesFolder =
            Path.Combine(
                root,
                "Images/Negative_FOV"
            );

        negativeLabelsFolder =
            Path.Combine(
                root,
                "Labels/Negative_FOV"
            );

        misalignedImagesFolder =
            Path.Combine(
                root,
                "Images/Misaligned_Laser"
            );

        misalignedLabelsFolder =
            Path.Combine(
                root,
                "Labels/Misaligned_Laser"
            );

        Directory.CreateDirectory(
            positiveImagesFolder
        );

        Directory.CreateDirectory(
            positiveLabelsFolder
        );

        Directory.CreateDirectory(
            negativeImagesFolder
        );

        Directory.CreateDirectory(
            negativeLabelsFolder
        );

        Directory.CreateDirectory(
            misalignedImagesFolder
        );

        Directory.CreateDirectory(
            misalignedLabelsFolder
        );
    }


    // ============================================================
    // FIND EXISTING IMAGES
    //
    // FIX: prefixes here must match EXACTLY what SaveImageAndLabel()
    // writes, or resume/counting silently breaks per category.
    // ============================================================

    void FindNextNumbers()
    {
        positiveCount =
            CountImages(
                positiveImagesFolder,
                "positive_"
            );

        negativeCount =
            CountImages(
                negativeImagesFolder,
                "negative_fov_"
            );

        misalignedCount =
            CountImages(
                misalignedImagesFolder,
                "misaligned_"
            );
    }


    int CountImages(
        string folder,
        string prefix
    )
    {
        if (!Directory.Exists(folder))
            return 0;

        return Directory.GetFiles(
            folder,
            prefix + "*.jpg"
        ).Length;
    }


    // ============================================================
    // SAVE ORIGINAL STATE
    // ============================================================

    void SaveOriginalState()
    {
        originalTargetPosition =
            targetSatellite.position;

        originalTargetRotation =
            targetSatellite.rotation;

        originalCameraPosition =
            datasetCamera.transform.position;

        originalCameraRotation =
            datasetCamera.transform.rotation;

        originalCameraFOV =
            datasetCamera.fieldOfView;

        originalLaserPosition =
            laserOrigin.position;

        originalLaserRotation =
            laserOrigin.rotation;

        originalBeamPosition =
            laserBeam.position;

        originalBeamRotation =
            laserBeam.rotation;

        originalBeamScale =
            laserBeam.localScale;
    }


    // ============================================================
    // START GENERATION
    // ============================================================

    public void StartGeneration()
    {
        if (isGenerating)
            return;

        if (!ValidateReferences())
            return;

        if (
            positiveCount >= positiveFOVImages &&
            negativeCount >= negativeFOVImages &&
            misalignedCount >= misalignedLaserImages
        )
        {
            Debug.Log(
                "DATASET ALREADY COMPLETE."
            );

            return;
        }

        isGenerating = true;

        generationCoroutine =
            StartCoroutine(
                GenerateDataset()
            );
    }


    // ============================================================
    // STOP
    // ============================================================

    public void StopGeneration()
    {
        if (!isGenerating)
            return;

        isGenerating = false;

        if (generationCoroutine != null)
        {
            StopCoroutine(
                generationCoroutine
            );

            generationCoroutine = null;
        }

        RestoreOriginalState();

        Debug.Log(
            "DATASET GENERATION STOPPED."
        );
    }


    // ============================================================
    // MAIN GENERATION
    //
    // FIX: each loop's category argument is now labeled explicitly
    // with an inline comment naming what it produces, so a future
    // edit can't silently swap two categories without it being obvious.
    // ============================================================

    IEnumerator GenerateDataset()
    {
        // --------------------------------------------------------
        // POSITIVE FOV — satellite inside frame, laser ALIGNED
        // --------------------------------------------------------

        while (
            isGenerating &&
            positiveCount < positiveFOVImages
        )
        {
            yield return StartCoroutine(
                CaptureCategory(
                    DatasetCategory.PositiveFOV // <-- must stay PositiveFOV
                )
            );

            yield return new WaitForSeconds(
                delayBetweenImages
            );
        }


        // --------------------------------------------------------
        // NEGATIVE FOV — satellite outside frame, empty label
        // --------------------------------------------------------

        while (
            isGenerating &&
            negativeCount < negativeFOVImages
        )
        {
            yield return StartCoroutine(
                CaptureCategory(
                    DatasetCategory.NegativeFOV // <-- must stay NegativeFOV
                )
            );

            yield return new WaitForSeconds(
                delayBetweenImages
            );
        }


        // --------------------------------------------------------
        // MISALIGNED LASER — satellite inside frame, laser MISALIGNED
        // --------------------------------------------------------

        while (
            isGenerating &&
            misalignedCount < misalignedLaserImages
        )
        {
            yield return StartCoroutine(
                CaptureCategory(
                    DatasetCategory.MisalignedLaser // <-- must stay MisalignedLaser
                )
            );

            yield return new WaitForSeconds(
                delayBetweenImages
            );
        }


        // --------------------------------------------------------
        // COMPLETE
        // --------------------------------------------------------

        isGenerating = false;

        generationCoroutine = null;

        RestoreOriginalState();

        Debug.Log("======================================");
        Debug.Log("DATASET GENERATION COMPLETE");
        Debug.Log("======================================");

        Debug.Log(
            "Positive FOV: " +
            positiveCount
        );

        Debug.Log(
            "Negative FOV: " +
            negativeCount
        );

        Debug.Log(
            "Misaligned Laser: " +
            misalignedCount
        );

        Debug.Log(
            "TOTAL: " +
            (
                positiveCount +
                negativeCount +
                misalignedCount
            )
        );

        Debug.Log("======================================");
    }


    // ============================================================
    // CAPTURE CATEGORY
    //
    // FIX 1: rotation and motion are applied BEFORE placement,
    // because they change the satellite's world-space bounding box.
    //
    // FIX 2: retries internally (maxPlacementAttempts) instead of
    // giving up after a single failed attempt.
    //
    // FIX 3: the PositiveFOV / MisalignedLaser branches below are
    // now written as an explicit switch with named cases (instead
    // of if / else-if / else) specifically so it's impossible for
    // the two "place inside FOV" categories to be silently merged
    // or reordered — each case is self-contained and unambiguous.
    // ============================================================

    IEnumerator CaptureCategory(
        DatasetCategory category
    )
    {
        bool success = false;
        int attempt = 0;

        while (!success && attempt < maxPlacementAttempts)
        {
            attempt++;

            SetupCamera();

            // ----------------------------------------------------
            // ROTATION FIRST
            // ----------------------------------------------------

            if (randomizeRotation)
            {
                targetSatellite.rotation =
                    Random.rotation;
            }
            else
            {
                targetSatellite.rotation =
                    originalTargetRotation;
            }


            // ----------------------------------------------------
            // PLACE SATELLITE — explicit per-category switch
            // ----------------------------------------------------

            switch (category)
            {
                case DatasetCategory.PositiveFOV:
                    PlaceSatelliteInsideFOV();
                    break;

                case DatasetCategory.MisalignedLaser:
                    PlaceSatelliteInsideFOV();
                    break;

                case DatasetCategory.NegativeFOV:
                    PlaceSatelliteOutsideFOV();
                    break;
            }


            // ----------------------------------------------------
            // OPTIONAL MOTION
            // ----------------------------------------------------

            if (enableMotion)
            {
                targetSatellite.position +=
                    Random.insideUnitSphere *
                    motionAmount;
            }


            // ----------------------------------------------------
            // LASER — explicit per-category switch
            // ----------------------------------------------------

            switch (category)
            {
                case DatasetCategory.MisalignedLaser:
                    SetupMisalignedLaser();
                    break;

                case DatasetCategory.PositiveFOV:
                case DatasetCategory.NegativeFOV:
                    SetupAlignedLaser();
                    break;
            }


            // ----------------------------------------------------
            // OCCLUSION
            // ----------------------------------------------------

            SetupOcclusion();

            Physics.SyncTransforms();

            yield return null;

            yield return new WaitForEndOfFrame();


            // ----------------------------------------------------
            // VERIFY CATEGORY
            // ----------------------------------------------------

            bool validFOV;

            if (category == DatasetCategory.NegativeFOV)
            {
                validFOV = !IsSatelliteInsideFOV();
            }
            else
            {
                validFOV = IsSatelliteInsideFOV();
            }


            if (!validFOV)
            {
                DisableOccluder();
                continue; // retry with a fresh random configuration
            }


            // ----------------------------------------------------
            // SAVE IMAGE + LABEL
            // ----------------------------------------------------

            bool saved =
                SaveImageAndLabel(
                    category
                );

            if (!saved)
            {
                DisableOccluder();
                continue; // retry — label validation failed
            }

            success = true;

            DisableOccluder();
        }

        if (!success)
        {
            Debug.LogWarning(
                "Scene configuration failed validation after " +
                maxPlacementAttempts +
                " attempts for category " + category +
                ". Skipping this image."
            );
        }
    }


    // ============================================================
    // CAMERA SETUP
    // ============================================================

    void SetupCamera()
    {
        if (randomizeCameraPosition)
        {
            datasetCamera.transform.position =
                originalCameraPosition +
                Random.insideUnitSphere *
                cameraPositionVariation;
        }
        else
        {
            datasetCamera.transform.position =
                originalCameraPosition;
        }

        datasetCamera.transform.rotation =
            originalCameraRotation;


        if (randomizeCameraFOV)
        {
            datasetCamera.fieldOfView =
                Random.Range(
                    minFOV,
                    maxFOV
                );
        }
        else
        {
            datasetCamera.fieldOfView =
                originalCameraFOV;
        }
    }


    // ============================================================
    // PLACE SATELLITE INSIDE FOV
    // ============================================================

    void PlaceSatelliteInsideFOV()
    {
        float distance =
            Random.Range(
                minDistance,
                maxDistance
            );


        float halfVerticalFOV =
            datasetCamera.fieldOfView *
            0.5f;


        float halfHorizontalFOV =
            Mathf.Atan(
                Mathf.Tan(
                    halfVerticalFOV *
                    Mathf.Deg2Rad
                ) *
                datasetCamera.aspect
            ) *
            Mathf.Rad2Deg;


        // Keep the satellite safely away from FOV edges.

        float horizontalAngle =
            Random.Range(
                -halfHorizontalFOV *
                (1f - positiveHorizontalMargin),

                halfHorizontalFOV *
                (1f - positiveHorizontalMargin)
            );


        float verticalAngle =
            Random.Range(
                -halfVerticalFOV *
                (1f - positiveVerticalMargin),

                halfVerticalFOV *
                (1f - positiveVerticalMargin)
            );


        Vector3 direction =
            Quaternion.Euler(
                verticalAngle,
                horizontalAngle,
                0f
            ) *
            datasetCamera.transform.forward;


        direction.Normalize();


        targetSatellite.position =
            datasetCamera.transform.position +
            direction *
            distance;


        // Final safety check.
        // If the whole satellite is not inside,
        // put it directly in the centre.

        if (!IsSatelliteInsideFOV())
        {
            targetSatellite.position =
                datasetCamera.transform.position +
                datasetCamera.transform.forward *
                distance;
        }
    }


    // ============================================================
    // PLACE SATELLITE OUTSIDE FOV
    // ============================================================

    void PlaceSatelliteOutsideFOV()
    {
        float distance =
            Random.Range(
                minDistance,
                maxDistance
            );


        float halfVerticalFOV =
            datasetCamera.fieldOfView *
            0.5f;


        float halfHorizontalFOV =
            Mathf.Atan(
                Mathf.Tan(
                    halfVerticalFOV *
                    Mathf.Deg2Rad
                ) *
                datasetCamera.aspect
            ) *
            Mathf.Rad2Deg;


        int side =
            Random.Range(
                0,
                4
            );


        float horizontalAngle = 0f;

        float verticalAngle = 0f;


        switch (side)
        {
            // RIGHT

            case 0:

                horizontalAngle =
                    halfHorizontalFOV +
                    negativeFOVAngle +
                    Random.Range(0f, 15f);

                verticalAngle =
                    Random.Range(
                        -10f,
                        10f
                    );

                break;


            // LEFT

            case 1:

                horizontalAngle =
                    -halfHorizontalFOV -
                    negativeFOVAngle -
                    Random.Range(0f, 15f);

                verticalAngle =
                    Random.Range(
                        -10f,
                        10f
                    );

                break;


            // TOP

            case 2:

                verticalAngle =
                    halfVerticalFOV +
                    negativeFOVAngle +
                    Random.Range(0f, 15f);

                horizontalAngle =
                    Random.Range(
                        -15f,
                        15f
                    );

                break;


            // BOTTOM

            case 3:

                verticalAngle =
                    -halfVerticalFOV -
                    negativeFOVAngle -
                    Random.Range(0f, 15f);

                horizontalAngle =
                    Random.Range(
                        -15f,
                        15f
                    );

                break;
        }


        Vector3 direction =
            Quaternion.Euler(
                verticalAngle,
                horizontalAngle,
                0f
            ) *
            datasetCamera.transform.forward;


        direction.Normalize();


        targetSatellite.position =
            datasetCamera.transform.position +
            direction *
            distance;


        // --------------------------------------------------------
        // GUARANTEE OUTSIDE FOV
        // --------------------------------------------------------

        int attempts = 0;

        while (
            IsSatelliteInsideFOV() &&
            attempts < 50
        )
        {
            horizontalAngle *= 1.15f;

            verticalAngle *= 1.15f;


            direction =
                Quaternion.Euler(
                    verticalAngle,
                    horizontalAngle,
                    0f
                ) *
                datasetCamera.transform.forward;


            direction.Normalize();


            targetSatellite.position =
                datasetCamera.transform.position +
                direction *
                distance;


            attempts++;
        }
    }


    // ============================================================
    // SATELLITE FOV CHECK
    // ============================================================

    bool IsSatelliteInsideFOV()
    {
        Renderer[] renderers =
            targetSatellite.GetComponentsInChildren<Renderer>();


        if (renderers.Length == 0)
            return false;


        Plane[] planes =
            GeometryUtility.CalculateFrustumPlanes(
                datasetCamera
            );


        Bounds totalBounds =
            renderers[0].bounds;


        for (
            int i = 1;
            i < renderers.Length;
            i++
        )
        {
            totalBounds.Encapsulate(
                renderers[i].bounds
            );
        }


        return GeometryUtility.TestPlanesAABB(
            planes,
            totalBounds
        );
    }


    // ============================================================
    // ALIGNED LASER
    // ============================================================

    void SetupAlignedLaser()
    {
        if (
            laserOrigin == null ||
            laserBeam == null
        )
            return;


        Vector3 start =
            laserOrigin.position;

        Vector3 end =
            targetSatellite.position;


        Vector3 direction =
            end - start;


        float distance =
            direction.magnitude;


        if (distance < 0.001f)
            return;


        direction.Normalize();


        // --------------------------------------------------------
        // POINT LASER ORIGIN TOWARD SATELLITE
        // --------------------------------------------------------

        laserOrigin.rotation =
            Quaternion.LookRotation(
                direction,
                laserOrigin.up
            );


        // --------------------------------------------------------
        // POSITION BEAM
        // --------------------------------------------------------

        laserBeam.position =
            (start + end) * 0.5f;


        // Cylinder's length axis is Y.

        laserBeam.rotation =
            Quaternion.FromToRotation(
                Vector3.up,
                direction
            );


        // Default Unity cylinder height = 2.

        float radius = 0.01f;

        laserBeam.localScale =
            new Vector3(
                radius,
                distance * 0.5f,
                radius
            );
    }


    // ============================================================
    // MISALIGNED LASER
    // ============================================================

    void SetupMisalignedLaser()
    {
        if (
            laserOrigin == null ||
            laserBeam == null
        )
            return;


        Vector3 start =
            laserOrigin.position;


        Vector3 targetDirection =
            targetSatellite.position -
            start;


        float distance =
            targetDirection.magnitude;


        if (distance < 0.001f)
            return;


        targetDirection.Normalize();


        // --------------------------------------------------------
        // RANDOM PERPENDICULAR AXIS
        // --------------------------------------------------------

        Vector3 randomAxis =
            Random.onUnitSphere;


        randomAxis =
            Vector3.Cross(
                targetDirection,
                randomAxis
            );


        if (
            randomAxis.sqrMagnitude <
            0.001f
        )
        {
            randomAxis =
                Vector3.Cross(
                    targetDirection,
                    Vector3.up
                );
        }


        randomAxis.Normalize();


        // --------------------------------------------------------
        // MISALIGNMENT ANGLE
        // --------------------------------------------------------

        float angle =
            Random.Range(
                minLaserMisalignment,
                maxLaserMisalignment
            );


        Vector3 misalignedDirection =
            Quaternion.AngleAxis(
                angle,
                randomAxis
            ) *
            targetDirection;


        misalignedDirection.Normalize();


        // --------------------------------------------------------
        // ROTATE LASER ORIGIN
        // --------------------------------------------------------

        laserOrigin.rotation =
            Quaternion.LookRotation(
                misalignedDirection,
                laserOrigin.up
            );


        // --------------------------------------------------------
        // POSITION VISIBLE BEAM
        // --------------------------------------------------------

        Vector3 end =
            start +
            misalignedDirection *
            distance;


        laserBeam.position =
            (start + end) *
            0.5f;


        laserBeam.rotation =
            Quaternion.FromToRotation(
                Vector3.up,
                misalignedDirection
            );


        float radius = 0.01f;


        laserBeam.localScale =
            new Vector3(
                radius,
                distance * 0.5f,
                radius
            );
    }


    // ============================================================
    // OCCLUSION
    // ============================================================

    void SetupOcclusion()
    {
        if (
            !enableOcclusion ||
            occluder == null
        )
        {
            DisableOccluder();
            return;
        }


        if (
            Random.value >
            occlusionProbability
        )
        {
            DisableOccluder();
            return;
        }


        occluder.SetActive(true);


        Vector3 cameraPosition =
            datasetCamera.transform.position;


        Vector3 satellitePosition =
            targetSatellite.position;


        Vector3 direction =
            satellitePosition -
            cameraPosition;


        float distance =
            direction.magnitude;


        if (distance < 0.001f)
        {
            DisableOccluder();
            return;
        }


        direction.Normalize();


        float occluderDistance =
            Random.Range(
                distance * 0.35f,
                distance * 0.75f
            );


        occluder.transform.position =
            cameraPosition +
            direction *
            occluderDistance;


        float scale =
            Random.Range(
                minOccluderScale,
                maxOccluderScale
            );


        occluder.transform.localScale =
            Vector3.one *
            scale;
    }


    // ============================================================
    // DISABLE OCCLUDER
    // ============================================================

    void DisableOccluder()
    {
        if (occluder != null)
        {
            occluder.SetActive(false);
        }
    }


    // ============================================================
    // SAVE IMAGE + LABEL
    //
    // FIX 1: prefix for MisalignedLaser corrected from
    // "negative_laser_" to "misaligned_" so it matches what
    // FindNextNumbers() scans for on restart.
    //
    // FIX 2: rewritten as an explicit switch (instead of
    // if / else-if / else) with one case per enum value, so each
    // category's folder/prefix/counter mapping is unambiguous and
    // cannot silently fall through to the wrong branch.
    // ============================================================

    bool SaveImageAndLabel(
        DatasetCategory category
    )
    {
        string prefix;

        string imageFolder;

        string labelFolder;

        int number;


        switch (category)
        {
            case DatasetCategory.PositiveFOV:

                prefix = "positive_";
                imageFolder = positiveImagesFolder;
                labelFolder = positiveLabelsFolder;
                number = positiveCount;
                break;

            case DatasetCategory.NegativeFOV:

                prefix = "negative_fov_";
                imageFolder = negativeImagesFolder;
                labelFolder = negativeLabelsFolder;
                number = negativeCount;
                break;

            case DatasetCategory.MisalignedLaser:

                prefix = "misaligned_"; // was "negative_laser_"
                imageFolder = misalignedImagesFolder;
                labelFolder = misalignedLabelsFolder;
                number = misalignedCount;
                break;

            default:

                Debug.LogError(
                    "SaveImageAndLabel: unhandled category " + category
                );

                return false;
        }


        string fileName =
            prefix +
            number.ToString("D5");


        string imagePath =
            Path.Combine(
                imageFolder,
                fileName + ".jpg"
            );


        string labelPath =
            Path.Combine(
                labelFolder,
                fileName + ".txt"
            );


        // --------------------------------------------------------
        // CREATE RENDER TEXTURE
        // --------------------------------------------------------

        RenderTexture renderTexture =
            new RenderTexture(
                width,
                height,
                24
            );


        renderTexture.Create();


        Texture2D image =
            new Texture2D(
                width,
                height,
                TextureFormat.RGB24,
                false
            );


        datasetCamera.targetTexture =
            renderTexture;


        RenderTexture.active =
            renderTexture;


        // --------------------------------------------------------
        // RENDER
        // --------------------------------------------------------

        datasetCamera.Render();


        image.ReadPixels(
            new Rect(
                0,
                0,
                width,
                height
            ),
            0,
            0
        );


        image.Apply();


        // --------------------------------------------------------
        // NOISE
        // --------------------------------------------------------

        if (enableNoise)
        {
            ApplyNoise(image);
        }


        // --------------------------------------------------------
        // CREATE LABEL FIRST
        //
        // IMPORTANT:
        // We do NOT save the image until the label
        // has been successfully validated.
        // --------------------------------------------------------

        bool labelValid = true;


        if (
            category !=
            DatasetCategory.NegativeFOV
        )
        {
            labelValid =
                CreateYOLOLabel(
                    targetSatellite,
                    datasetCamera,
                    labelPath
                );


            if (!labelValid)
            {
                Debug.LogWarning(
                    "LABEL CREATION FAILED. IMAGE NOT SAVED: " +
                    fileName
                );

                CleanupRenderTexture(
                    renderTexture,
                    image
                );

                return false;
            }
        }
        else
        {
            // Negative FOV:
            // No satellite in image.
            //
            // YOLO requires an EMPTY label file.

            File.WriteAllText(
                labelPath,
                ""
            );
        }


        // --------------------------------------------------------
        // SAVE IMAGE
        // --------------------------------------------------------

        byte[] bytes =
            image.EncodeToJPG(
                jpegQuality
            );


        File.WriteAllBytes(
            imagePath,
            bytes
        );


        // --------------------------------------------------------
        // CLEANUP
        // --------------------------------------------------------

        CleanupRenderTexture(
            renderTexture,
            image
        );


        // --------------------------------------------------------
        // INCREMENT COUNT
        // --------------------------------------------------------

        switch (category)
        {
            case DatasetCategory.PositiveFOV:
                positiveCount++;
                break;

            case DatasetCategory.NegativeFOV:
                negativeCount++;
                break;

            case DatasetCategory.MisalignedLaser:
                misalignedCount++;
                break;
        }


        Debug.Log(
            "IMAGE SAVED: " +
            fileName +
            " | CATEGORY: " +
            category
        );


        return true;
    }


    // ============================================================
    // CLEANUP RENDER TEXTURE
    // ============================================================

    void CleanupRenderTexture(
        RenderTexture renderTexture,
        Texture2D image
    )
    {
        datasetCamera.targetTexture =
            null;

        RenderTexture.active =
            null;


        if (renderTexture != null)
        {
            Destroy(renderTexture);
        }


        if (image != null)
        {
            Destroy(image);
        }
    }


    // ============================================================
    // CREATE YOLO LABEL
    // ============================================================

    bool CreateYOLOLabel(
        Transform target,
        Camera cam,
        string labelPath
    )
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>();


        if (renderers.Length == 0)
            return false;


        float minX = 1f;

        float maxX = 0f;

        float minY = 1f;

        float maxY = 0f;

        bool hasVisiblePoint = false;


        // --------------------------------------------------------
        // CALCULATE BOUNDING BOX
        // --------------------------------------------------------

        foreach (
            Renderer renderer
            in renderers
        )
        {
            Bounds bounds =
                renderer.bounds;


            Vector3 center =
                bounds.center;

            Vector3 extents =
                bounds.extents;


            Vector3[] corners =
                GetBoundsCorners(
                    center,
                    extents
                );


            foreach (
                Vector3 corner
                in corners
            )
            {
                Vector3 viewport =
                    cam.WorldToViewportPoint(
                        corner
                    );


                if (
                    viewport.z > 0f
                )
                {
                    hasVisiblePoint =
                        true;


                    minX =
                        Mathf.Min(
                            minX,
                            viewport.x
                        );

                    maxX =
                        Mathf.Max(
                            maxX,
                            viewport.x
                        );

                    minY =
                        Mathf.Min(
                            minY,
                            viewport.y
                        );

                    maxY =
                        Mathf.Max(
                            maxY,
                            viewport.y
                        );
                }
            }
        }


        if (!hasVisiblePoint)
            return false;


        // --------------------------------------------------------
        // REQUIRE COMPLETE SATELLITE INSIDE FOV
        // --------------------------------------------------------

        if (
            minX < 0f ||
            maxX > 1f ||
            minY < 0f ||
            maxY > 1f
        )
        {
            return false;
        }


        minX =
            Mathf.Clamp01(minX);

        maxX =
            Mathf.Clamp01(maxX);

        minY =
            Mathf.Clamp01(minY);

        maxY =
            Mathf.Clamp01(maxY);


        float boxWidth =
            maxX - minX;

        float boxHeight =
            maxY - minY;


        if (
            boxWidth <= 0.001f ||
            boxHeight <= 0.001f
        )
        {
            return false;
        }


        float centerX =
            (minX + maxX) *
            0.5f;


        float centerY =
            1f -
            (
                (minY + maxY) *
                0.5f
            );


        // --------------------------------------------------------
        // YOLO FORMAT
        //
        // class centerX centerY width height
        // --------------------------------------------------------

        string label =
            "0 " +
            centerX.ToString(
                "F6",
                CultureInfo.InvariantCulture
            ) +
            " " +
            centerY.ToString(
                "F6",
                CultureInfo.InvariantCulture
            ) +
            " " +
            boxWidth.ToString(
                "F6",
                CultureInfo.InvariantCulture
            ) +
            " " +
            boxHeight.ToString(
                "F6",
                CultureInfo.InvariantCulture
            );


        File.WriteAllText(
            labelPath,
            label
        );


        return true;
    }


    // ============================================================
    // BOUNDS CORNERS
    // ============================================================

    Vector3[] GetBoundsCorners(
        Vector3 center,
        Vector3 extents
    )
    {
        return new Vector3[]
        {
            center + new Vector3(
                -extents.x,
                -extents.y,
                -extents.z
            ),

            center + new Vector3(
                -extents.x,
                -extents.y,
                extents.z
            ),

            center + new Vector3(
                -extents.x,
                extents.y,
                -extents.z
            ),

            center + new Vector3(
                -extents.x,
                extents.y,
                extents.z
            ),

            center + new Vector3(
                extents.x,
                -extents.y,
                -extents.z
            ),

            center + new Vector3(
                extents.x,
                -extents.y,
                extents.z
            ),

            center + new Vector3(
                extents.x,
                extents.y,
                -extents.z
            ),

            center + new Vector3(
                extents.x,
                extents.y,
                extents.z
            )
        };
    }


    // ============================================================
    // NOISE
    // ============================================================

    void ApplyNoise(Texture2D image)
    {
        Color[] pixels =
            image.GetPixels();


        for (
            int i = 0;
            i < pixels.Length;
            i++
        )
        {
            float noise =
                Random.Range(
                    -noiseStrength,
                    noiseStrength
                );


            pixels[i].r =
                Mathf.Clamp01(
                    pixels[i].r +
                    noise
                );

            pixels[i].g =
                Mathf.Clamp01(
                    pixels[i].g +
                    noise
                );

            pixels[i].b =
                Mathf.Clamp01(
                    pixels[i].b +
                    noise
                );
        }


        image.SetPixels(
            pixels
        );

        image.Apply();
    }


    // ============================================================
    // SINGLE CAPTURE
    // ============================================================

    IEnumerator CaptureSingleImage()
    {
        yield return StartCoroutine(
            CaptureCategory(
                DatasetCategory.PositiveFOV
            )
        );
    }


    // ============================================================
    // RESTORE ORIGINAL STATE
    // ============================================================

    void RestoreOriginalState()
    {
        if (targetSatellite != null)
        {
            targetSatellite.position =
                originalTargetPosition;

            targetSatellite.rotation =
                originalTargetRotation;
        }


        if (datasetCamera != null)
        {
            datasetCamera.transform.position =
                originalCameraPosition;

            datasetCamera.transform.rotation =
                originalCameraRotation;

            datasetCamera.fieldOfView =
                originalCameraFOV;
        }


        if (laserOrigin != null)
        {
            laserOrigin.position =
                originalLaserPosition;

            laserOrigin.rotation =
                originalLaserRotation;
        }


        if (laserBeam != null)
        {
            laserBeam.position =
                originalBeamPosition;

            laserBeam.rotation =
                originalBeamRotation;

            laserBeam.localScale =
                originalBeamScale;
        }


        DisableOccluder();
    }


    // ============================================================
    // DISABLE
    // ============================================================

    void OnDisable()
    {
        if (isGenerating)
        {
            isGenerating = false;

            generationCoroutine =
                null;

            RestoreOriginalState();
        }
    }
}