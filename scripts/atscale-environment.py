from troposphere import Base64, FindInMap, GetAtt, Parameter, Ref, Template, Join, Output
import troposphere.dynamodb as dynamodb
import troposphere.s3 as s3
import troposphere.sqs as sqs
import troposphere.ec2 as ec2
import troposphere.iam as iam
import troposphere.cloudformation as cloudformation

template = Template()

image_bucket_name = "atscale-images-dev"
image_requests_table_name = "atscale-resize-requests-dev"
image_requests_queue_name = "atscale-image-requests-dev"

# Some config
octopus_tentacle_installer = "Octopus.Tentacle.2.6.4.951-x64.msi"
octopus_tentacle_download_url = "http://download.octopusdeploy.com/octopus/%s" % octopus_tentacle_installer

# Parameters
keyname_param = template.add_parameter(Parameter(
    "KeyName",
    Description="Name of an existing EC2 KeyPair to enable RDP access to the instance",
    Type="String"
))

octopus_master_url = template.add_parameter(Parameter(
    "OctopusMasterUrl",
    Description="URL of the Octopus Master",
    Type="String"
))

octopus_deploy_thumbprint = template.add_parameter(Parameter(
    "OctopusThumbprint",
    Description="Thumbprint of the Octopus Deploy Server",
    Type="String"
))

octopus_api_key = template.add_parameter(Parameter(
    "OctopusApiKey",
    Description="API key to use with the Octopus Deploy Server",
    Type="String"
))

# used for allowing traffic from the master to our tentacle
octopus_security_group_id = template.add_parameter(Parameter(
    "OctopusSecurityGroupId",
    Description="Id of the security group that the Octopus Master is attached to",
    Type="String"
))

template.add_mapping('RegionMap', {
    "us-west-2": {"AMI": "ami-59f2d769"},
})

# Bucket to hold the images
image_bucket = template.add_resource(s3.Bucket(
    "AtScaleImages",
    BucketName=image_bucket_name
))

image_bucket_policy = template.add_resource(s3.BucketPolicy(
    "AtScaleImagesPolicy",
    Bucket=Ref(image_bucket),
    PolicyDocument={
        "Statement": [
            {
                "Sid": "AllowPublicRead",
                "Effect": "Allow",
                "Principal": {
                    "AWS": "*"
                },
                "Action": "s3:GetObject",
                # Note that using Ref() means the bucket doesn't yet have to exist, and CloudFormation will work out ordering
                "Resource": Join("", ["arn:aws:s3:::", Ref(image_bucket), "/output*"])
            }
        ]
    }
))

template.add_output(Output(
    "ImageBucketDomainName",
    Description="Domain name of the AtScale image bucket",
    Value=GetAtt(image_bucket, "DomainName")
))

# DynamoDB table to track the image requests
image_table = template.add_resource(dynamodb.Table(
    "AtScaleResizeRequests",
    TableName=image_requests_table_name,
    AttributeDefinitions=[
        dynamodb.AttributeDefinition("ImageId", "S"),
    ],
    KeySchema=[
        dynamodb.Key("ImageId", "HASH")
    ],
    ProvisionedThroughput=dynamodb.ProvisionedThroughput(3, 1)
))

template.add_output(Output(
    "RequestsDynamoDBTableName",
    Description="Name of the DynamoDB table",
    # There's no way to get this with GetAtt() :(
    Value=image_requests_table_name
))

# SQS queue for resize jobs
resize_queue = template.add_resource(sqs.Queue(
    "AtScaleImagesResizeQueue",
    QueueName=image_requests_queue_name
))

template.add_output(Output(
    "ResizeQueueName",
    Description="SQS queue name of our image resize requests",
    Value=GetAtt(resize_queue, "QueueName")
))

# Instance role
web_instance_role = template.add_resource(iam.Role(
    "AtScaleWebRole",
    Path="/",
    AssumeRolePolicyDocument={
        "Statement": [{
                          "Effect": "Allow",
                          "Principal": {
                              "Service": "ec2.amazonaws.com"
                          },
                          "Action": "sts:AssumeRole"
                      }]
    },
    Policies=[
        iam.Policy(
            PolicyName="AccessDynamoDbSqsAndS3",
            PolicyDocument={
                "Statement": [
                    {
                        "Effect": "Allow",
                        "Action": [
                            "sqs:SendMessage"
                        ],
                        "Resource": [
                            GetAtt(resize_queue, "Arn")
                        ]
                    },
                    {
                        "Effect": "Allow",
                        "Action": [
                            "dynamodb:GetItem",
                            "dynamodb:PutItem",
                            "dynamodb:Query",
                            "dynamodb:UpdateItem"
                        ],
                        "Resource": [
                            # DynamoDB doesn't support GetAtt(..., "Arn") in cloud formation :(
                            Join(":", ["arn:aws:dynamodb", Ref("AWS::Region"), Ref("AWS::AccountId"),
                                       "table/%s" % image_requests_table_name])
                        ]
                    },
                    {
                        "Effect": "Allow",
                        "Action": [
                            "s3:PutObject"
                        ],
                        "Resource": [
                            "arn:aws:s3:::%s" % image_bucket_name
                        ]
                    }
                ]
            }
        )
    ],
))

web_instance_profile = template.add_resource(iam.InstanceProfile(
    "AtScaleWebInstanceProfile",
    Path="/",
    Roles=[Ref(web_instance_role)]
))

# Web security group
web_security_group = template.add_resource(ec2.SecurityGroup(
    "AtScaleWebSg",
    GroupDescription="Security group for AtScale web instances",
    SecurityGroupIngress=[
        ec2.SecurityGroupRule(
            IpProtocol="tcp",
            CidrIp="0.0.0.0/0",
            FromPort="8080",
            ToPort="8080"
        ),
        ec2.SecurityGroupRule(
            IpProtocol="tcp",
            CidrIp="0.0.0.0/0",
            FromPort="3389",
            ToPort="3389"
        ),
        # Octopus Deploy listening tentacle
        # Only allow connections from the OctopusMasterSg (see the operations template)
        ec2.SecurityGroupRule(
            IpProtocol="tcp",
            FromPort="10933",
            ToPort="10933",
            SourceSecurityGroupId=Ref(octopus_security_group_id)
        )
    ]
))

web_instance1 = template.add_resource(ec2.Instance(
    "Web1",
    ImageId=FindInMap("RegionMap", Ref("AWS::Region"), "AMI"),
    InstanceType="t1.micro",
    KeyName=Ref(keyname_param),
    SecurityGroups=[Ref(web_security_group)],
    IamInstanceProfile=Ref(web_instance_profile),
    UserData=Base64(Join('', [
        '<script>\n',
        'cfn-init -s "', Ref('AWS::StackName'), '" --region ', Ref("AWS::Region"), ' -r Web1 -c ascending\n',
        '</script>\n',
        '<powershell>\n',
        'New-NetFirewallRule -Displayname "Allow Octopus Deploy Connections" -Direction inbound -LocalPort 10933 -Protocol TCP -Action Allow\n',

        'pushd "C:\\Program Files\\Octopus Deploy\\Tentacle"\n',
        '$downloader = New-Object System.Net.WebClient\n',
        # Metadata endpoint for EC2 instances, see http://docs.aws.amazon.com/AWSEC2/latest/UserGuide/ec2-instance-metadata.html
        '$ipAddress = $downloader.DownloadString("http://169.254.169.254/latest/meta-data/local-ipv4").Trim()\n',

        '.\Tentacle.exe create-instance --instance "Tentacle" --config "C:\\Octopus\\Tentacle\\Tentacle.config" --console\n',
        '.\Tentacle.exe new-certificate --instance "Tentacle" --console\n',
        '.\Tentacle.exe configure --instance "Tentacle" --home "C:\\Octopus" --console\n'
        '.\Tentacle.exe configure --instance "Tentacle" --app "C:\\Octopus\\Applications" --console\n',
        '.\Tentacle.exe configure --instance "Tentacle" --port "10933" --console\n',
        '.\Tentacle.exe configure --instance "Tentacle" --trust "', Ref(octopus_deploy_thumbprint), '" --console\n',

        '.\Tentacle.exe register-with --instance "Tentacle" --server "', Ref(octopus_master_url), '"',
        ' --apiKey="', Ref(octopus_api_key), '"',
        ' --role "web-server" --environment "Dev"',
        ' --publicHostName $ipAddress',
        ' --comms-style TentaclePassive --console\n',

        '.\Tentacle.exe service --instance "Tentacle" --install --start --console\n',
        'popd\n',
        '</powershell>\n'
    ])),
    Metadata=cloudformation.Metadata(
        cloudformation.Init(
            cloudformation.InitConfigSets(
                ascending=['config1'],
                descending=['config1']
            ),
            config1=cloudformation.InitConfig(
                files={
                    r"c:\Packages\%s" % octopus_tentacle_installer: {
                        "source": octopus_tentacle_download_url
                    }
                },
                commands={
                    "1-install-octopus-tentacle": {
                        "command": r'msiexec.exe /i "c:\Packages\%s" /quiet' % octopus_tentacle_installer
                    }
                }
            )
        )
    )
))

template.add_output(Output(
    "Web1PrivateDns",
    Description="Private DNS name of Web1",
    Value=GetAtt(web_instance1, "PrivateDnsName")
))

template.add_output(Output(
    "URL",
    Description="URL of AtScale",
    Value=Join("", ["http://", GetAtt(web_instance1, "PublicDnsName")])
))

# Worker EC2 instance
worker_instance_role = template.add_resource(iam.Role(
    "AtScaleWorkerRole",
    Path="/",
    AssumeRolePolicyDocument={
        "Statement": [{
                          "Effect": "Allow",
                          "Principal": {
                              "Service": "ec2.amazonaws.com"
                          },
                          "Action": "sts:AssumeRole"
                      }]
    },
    Policies=[
        iam.Policy(
            PolicyName="AccessDynamoDbSqsAndS3",
            PolicyDocument={
                "Statement": [
                    {
                        "Effect": "Allow",
                        "Action": [
                            "sqs:ReceiveMessage",
                            "sqs:DeleteMessage",
                        ],
                        "Resource": [
                            GetAtt(resize_queue, "Arn")
                        ]
                    },
                    {
                        "Effect": "Allow",
                        "Action": [
                            "dynamodb:GetItem",
                            "dynamodb:PutItem",
                            "dynamodb:Query",
                            "dynamodb:UpdateItem"
                        ],
                        "Resource": [
                            # DynamoDB doesn't support GetAtt(..., "Arn") in cloud formation :(
                            Join(":", ["arn:aws:dynamodb", Ref("AWS::Region"), Ref("AWS::AccountId"),
                                       "table/%s" % image_requests_table_name])
                        ]
                    },
                    {
                        "Effect": "Allow",
                        "Action": [
                            "s3:PutObject",
                            "s3:GetObject"
                        ],
                        "Resource": [
                            "arn:aws:s3:::%s" % image_bucket_name
                        ]
                    }
                ]
            }
        )
    ]
))

worker_instance_profile = template.add_resource(iam.InstanceProfile(
    "AtScaleWorkerInstanceProfile",
    Path="/",
    Roles=[Ref(worker_instance_role)]
))

worker_security_group = template.add_resource(ec2.SecurityGroup(
    "AtScaleWorkerSg",
    GroupDescription="Security group for AtScale worker instances",
    SecurityGroupIngress=[
        # Octopus Deploy listening tentacle
        # Only allow connections from the OctopusMasterSg (see the operations template)
        ec2.SecurityGroupRule(
            IpProtocol="tcp",
            FromPort="10933",
            ToPort="10933",
            SourceSecurityGroupId=Ref(octopus_security_group_id)
        )
    ]
))

worker_instance1 = template.add_resource(ec2.Instance(
    "Worker1",
    ImageId=FindInMap("RegionMap", Ref("AWS::Region"), "AMI"),
    InstanceType="t1.micro",
    KeyName=Ref(keyname_param),
    IamInstanceProfile=Ref(worker_instance_profile),
    SecurityGroups=[Ref(worker_security_group)],
    UserData=Base64(Join('', [
        '<script>\n',
        'cfn-init -s "', Ref('AWS::StackName'), '" --region ', Ref("AWS::Region"), ' -r Worker1 -c ascending\n',
        '</script>\n',
        '<powershell>\n',
        'New-NetFirewallRule -Displayname "Allow Octopus Deploy Connections" -Direction inbound -LocalPort 10933 -Protocol TCP -Action Allow\n',

        'pushd "C:\\Program Files\\Octopus Deploy\\Tentacle"\n',
        '$downloader = New-Object System.Net.WebClient\n',
        # Metadata endpoint for EC2 instances, see http://docs.aws.amazon.com/AWSEC2/latest/UserGuide/ec2-instance-metadata.html
        '$ipAddress = $downloader.DownloadString("http://169.254.169.254/latest/meta-data/local-ipv4").Trim()\n',

        '.\Tentacle.exe create-instance --instance "Tentacle" --config "C:\\Octopus\\Tentacle\\Tentacle.config" --console\n',
        '.\Tentacle.exe new-certificate --instance "Tentacle" --console\n',
        '.\Tentacle.exe configure --instance "Tentacle" --home "C:\\Octopus" --console\n'
        '.\Tentacle.exe configure --instance "Tentacle" --app "C:\\Octopus\\Applications" --console\n',
        '.\Tentacle.exe configure --instance "Tentacle" --port "10933" --console\n',
        '.\Tentacle.exe configure --instance "Tentacle" --trust "', Ref(octopus_deploy_thumbprint), '" --console\n',

        '.\Tentacle.exe register-with --instance "Tentacle" --server "', Ref(octopus_master_url), '"',
        ' --apiKey="', Ref(octopus_api_key), '"',
        ' --role "worker-server" --environment "Dev"',
        ' --publicHostName $ipAddress',
        ' --comms-style TentaclePassive --console\n',

        '.\Tentacle.exe service --instance "Tentacle" --install --start --console\n',
        'popd\n',
        '</powershell>\n'
    ])),
    Metadata=cloudformation.Metadata(
        cloudformation.Init(
            cloudformation.InitConfigSets(
                ascending=['config1'],
                descending=['config1']
            ),
            config1=cloudformation.InitConfig(
                files={
                    r"c:\Packages\%s" % octopus_tentacle_installer: {
                        "source": octopus_tentacle_download_url
                    }
                },
                commands={
                    "1-install-octopus-tentacle": {
                        "command": r'msiexec.exe /i "c:\Packages\%s" /quiet' % octopus_tentacle_installer
                    }
                }
            )
        )
    )
))

template.add_output(Output(
    "Worker1PrivateDns",
    Description="Private DNS name of Worker1",
    Value=GetAtt(worker_instance1, "PrivateDnsName")
))

with open('environment.json', 'w') as fh:
    contents = template.to_json()
    fh.write(contents)
